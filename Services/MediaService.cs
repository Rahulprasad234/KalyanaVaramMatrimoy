using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Services;

/// <summary>
/// P6–P9. Local-disk storage for development. Swap SaveFileAsync/DeleteFile for S3 / GCS / Azure Blob
/// in production; keep KYC files in a PRIVATE bucket and never return their keys to other members.
/// </summary>
public sealed class MediaService(AppDbContext db, IOptions<StorageOptions> options, IWebHostEnvironment env)
{
    private readonly StorageOptions _o = options.Value;

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/jpg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp"
    };

    private static readonly Dictionary<string, string> DocumentTypes = new(ImageTypes, StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf"
    };

    private static readonly Dictionary<string, string> VideoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = ".mp4", ["video/webm"] = ".webm", ["video/quicktime"] = ".mov"
    };

    private string PublicRoot => Path.GetFullPath(Path.Combine(env.ContentRootPath, _o.PublicRoot));
    private string PrivateRoot => Path.GetFullPath(Path.Combine(env.ContentRootPath, _o.PrivateRoot));

    // ---------- P6 ----------
    public async Task<PhotoUploadResponse> UploadPhotoAsync(long userId, IFormFile? file, string baseUrl, CancellationToken ct)
    {
        var ext = Check(file, "file", ImageTypes, _o.MaxPhotoBytes, "Only JPG, PNG or WEBP photos are allowed.");

        var photos = await ActivePhotos(userId).ToListAsync(ct);
        if (photos.Count >= _o.MaxPhotos)
            throw ApiException.Validation($"You can upload up to {_o.MaxPhotos} photos. Remove one to add another.", "file");

        var key = $"photos/{userId}/{Guid.NewGuid():N}{ext}";
        await SaveFileAsync(file!, Path.Combine(PublicRoot, key), ct);

        var url = $"{(_o.PublicBaseUrl ?? baseUrl).TrimEnd('/')}/uploads/{key}";
        db.Photos.Add(new Photo
        {
            UserId = userId, StorageKey = key, Url = url,
            SortOrder = (short)photos.Count, IsPrimary = photos.Count == 0
        });
        await db.SaveChangesAsync(ct);

        return new PhotoUploadResponse(url, await PhotoUrlsAsync(userId, ct));
    }

    // ---------- P7 ----------
    public async Task<PhotosResponse> DeletePhotoAsync(long userId, string url, CancellationToken ct)
    {
        var photos = await ActivePhotos(userId).ToListAsync(ct);
        var photo = photos.FirstOrDefault(p => p.Url == url)
                    ?? throw ApiException.NotFound("Photo not found.");

        photo.DeletedAt = DateTime.UtcNow;
        photo.IsPrimary = false;
        var remaining = photos.Where(p => p != photo).ToList();
        for (short i = 0; i < remaining.Count; i++)
        {
            remaining[i].SortOrder = i;
            remaining[i].IsPrimary = i == 0;
        }
        await db.SaveChangesAsync(ct);
        DeleteFile(Path.Combine(PublicRoot, photo.StorageKey));

        return new PhotosResponse(await PhotoUrlsAsync(userId, ct));
    }

    // ---------- P8 ----------
    public async Task<VerificationResponse> SubmitKycAsync(long userId, KycSubmitForm form, CancellationToken ct)
    {
        var latest = await LatestKyc(userId, ct);
        if (latest?.Status == KycStatus.Verified)
            throw ApiException.Conflict("Your profile is already verified.");

        var docExt = Check(form.DocumentFile, "documentFile", DocumentTypes, _o.MaxKycFileBytes, "ID must be a JPG, PNG, WEBP or PDF file.");
        var backExt = form.DocumentBackFile is null ? null
            : Check(form.DocumentBackFile, "documentBackFile", DocumentTypes, _o.MaxKycFileBytes, "ID must be a JPG, PNG, WEBP or PDF file.");
        var selfieExt = Check(form.SelfieFile, "selfieFile", ImageTypes, _o.MaxKycFileBytes, "Selfie must be a JPG, PNG or WEBP photo.");
        var videoExt = form.VideoIntroFile is null ? null
            : Check(form.VideoIntroFile, "videoIntroFile", VideoTypes, _o.MaxVideoBytes, "Video must be MP4, WEBM or MOV.");

        var folder = $"kyc/{userId}/{Guid.NewGuid():N}";
        var kyc = new KycVerification
        {
            UserId = userId,
            DocumentType = form.DocumentType!,
            DocFrontKey = $"{folder}/document-front{docExt}",
            DocBackKey = backExt is null ? null : $"{folder}/document-back{backExt}",
            SelfieKey = $"{folder}/selfie{selfieExt}",
            VideoKey = videoExt is null ? null : $"{folder}/video{videoExt}",
            Status = KycStatus.Pending,
            SubmittedAt = DateTime.UtcNow
        };

        await SaveFileAsync(form.DocumentFile!, Path.Combine(PrivateRoot, kyc.DocFrontKey), ct);
        if (kyc.DocBackKey is not null) await SaveFileAsync(form.DocumentBackFile!, Path.Combine(PrivateRoot, kyc.DocBackKey), ct);
        await SaveFileAsync(form.SelfieFile!, Path.Combine(PrivateRoot, kyc.SelfieKey), ct);
        if (kyc.VideoKey is not null) await SaveFileAsync(form.VideoIntroFile!, Path.Combine(PrivateRoot, kyc.VideoKey), ct);

        db.KycVerifications.Add(kyc);
        await db.SaveChangesAsync(ct);
        return ToResponse(kyc);
    }

    // ---------- P9 ----------
    public async Task<VerificationResponse> GetKycAsync(long userId, CancellationToken ct) =>
        ToResponse(await LatestKyc(userId, ct));

    /// <summary>Development helper: mark the latest KYC as verified / rejected.</summary>
    public async Task<VerificationResponse> ReviewKycAsync(string memberId, string status, string? reason, CancellationToken ct)
    {
        var userId = await db.Users.Where(u => u.MemberId == memberId).Select(u => (long?)u.Id).FirstOrDefaultAsync(ct)
                     ?? throw ApiException.NotFound("Member not found.");
        var kyc = await db.KycVerifications.Where(k => k.UserId == userId)
                      .OrderByDescending(k => k.SubmittedAt).FirstOrDefaultAsync(ct)
                  ?? throw ApiException.NotFound("No verification submitted.");
        kyc.Status = status;
        kyc.RejectionReason = status == KycStatus.Rejected ? reason : null;
        kyc.ReviewedAt = DateTime.UtcNow;
        kyc.ReviewedBy = "dev";
        await db.SaveChangesAsync(ct);
        return ToResponse(kyc);
    }

    private IQueryable<Photo> ActivePhotos(long userId) =>
        db.Photos.Where(p => p.UserId == userId && p.DeletedAt == null).OrderBy(p => p.SortOrder);

    private Task<List<string>> PhotoUrlsAsync(long userId, CancellationToken ct) =>
        ActivePhotos(userId).Select(p => p.Url).ToListAsync(ct);

    private Task<KycVerification?> LatestKyc(long userId, CancellationToken ct) =>
        db.KycVerifications.Where(k => k.UserId == userId).OrderByDescending(k => k.SubmittedAt).FirstOrDefaultAsync(ct);

    private static VerificationResponse ToResponse(KycVerification? k) => k is null
        ? new VerificationResponse(KycStatus.NotSubmitted, null, false, null)
        : new VerificationResponse(k.Status, k.DocumentType, !string.IsNullOrEmpty(k.SelfieKey), k.SubmittedAt);

    private static string Check(IFormFile? file, string field, Dictionary<string, string> allowed, long maxBytes, string typeMessage)
    {
        if (file is null || file.Length == 0) throw ApiException.Validation("Please choose a file.", field);
        if (file.Length > maxBytes)
            throw ApiException.PayloadTooLarge($"File is too large. Maximum size is {maxBytes / (1024 * 1024)} MB.");
        if (!allowed.TryGetValue(file.ContentType, out var ext)) throw ApiException.UnsupportedMediaType(typeMessage);
        return ext;
    }

    private static async Task SaveFileAsync(IFormFile file, string fullPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var stream = File.Create(fullPath);
        await file.CopyToAsync(stream, ct);
    }

    private static void DeleteFile(string fullPath)
    {
        try { if (File.Exists(fullPath)) File.Delete(fullPath); }
        catch (IOException) { /* best effort */ }
    }
}
