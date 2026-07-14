namespace Portfolio.Services;

public sealed record ValidatedUpload(string Extension, string MimeType);

public static class UploadFileValidator
{
    private static readonly IReadOnlyDictionary<string, string> ImageMimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif"
        };

    public static async Task<ValidatedUpload?> ValidateImageAsync(
        IFormFile file,
        params string[] allowedExtensions)
    {
        if (file.Length <= 0)
            return null;

        var extension = Path.GetExtension(GetSafeFileName(file.FileName)).ToLowerInvariant();
        if (!ImageMimeTypes.TryGetValue(extension, out var expectedMimeType))
            return null;

        if (allowedExtensions.Length > 0 &&
            !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return null;

        if (!string.Equals(file.ContentType, expectedMimeType, StringComparison.OrdinalIgnoreCase))
            return null;

        var header = await ReadHeaderAsync(file, 12);
        if (!HasExpectedImageSignature(header, extension))
            return null;

        return new ValidatedUpload(extension, expectedMimeType);
    }

    public static async Task<bool> ValidatePdfAsync(IFormFile file)
    {
        if (file.Length <= 0 ||
            !string.Equals(Path.GetExtension(GetSafeFileName(file.FileName)), ".pdf", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var header = await ReadHeaderAsync(file, 5);
        return header.Length >= 5 &&
               header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 &&
               header[3] == 0x46 && header[4] == 0x2D;
    }

    public static string GetSafeFileName(string submittedFileName)
    {
        var normalized = submittedFileName.Replace('\\', '/');
        return Path.GetFileName(normalized);
    }

    private static async Task<byte[]> ReadHeaderAsync(IFormFile file, int length)
    {
        var buffer = new byte[length];
        var totalRead = 0;

        await using var stream = file.OpenReadStream();
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead == buffer.Length ? buffer : buffer[..totalRead];
    }

    private static bool HasExpectedImageSignature(byte[] header, string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => header.Length >= 3 &&
                                   header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => header.Length >= 8 &&
                      header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                      header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            ".gif" => header.Length >= 6 &&
                      header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38 &&
                      (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61,
            ".webp" => header.Length >= 12 &&
                       header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                       header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50,
            _ => false
        };
    }
}
