using System;
using System.Collections.Generic;
using System.Text;
using SkiaSharp;
using Usterki.Infrastructure.Storage;

namespace Usterki.Infrastructure.Photos;

public sealed class PhotoProcessor
{
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    private const long MaxPixels = 24_000_000;
    private const int MaxDimension = 12_000;
    private const int MaxOutputDimension = 2560;

    public ProcessedPhoto Process(byte[] content)
    {
        if (content.Length == 0 || content.Length > MaxUploadBytes)
        {
            throw new PhotoValidationException(
                "Zdjęcie musi mieć od 1 bajta do 10 MiB.");
        }

        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);

        if (codec is null)
        {
            throw new PhotoValidationException(
                "Nie można odczytać obrazu.");
        }

        if (codec.EncodedFormat != SKEncodedImageFormat.Jpeg &&
            codec.EncodedFormat != SKEncodedImageFormat.Png)
        {
            throw new PhotoValidationException(
                "Dozwolone są wyłącznie zdjęcia JPEG i PNG.");
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;

        if (width <= 0 ||
            height <= 0 ||
            width > MaxDimension ||
            height > MaxDimension ||
            (long)width * height > MaxPixels)
        {
            throw new PhotoValidationException(
                "Obraz przekracza dozwolone wymiary lub 24 miliony pikseli.");
        }

        if (codec.FrameCount > 1)
        {
            throw new PhotoValidationException(
                "Obrazy animowane nie są obsługiwane.");
        }

        var decodedInfo = new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var decoded = new SKBitmap(decodedInfo);

        var result = codec.GetPixels(
            decodedInfo,
            decoded.GetPixels());

        if (result != SKCodecResult.Success)
        {
            throw new PhotoValidationException(
                "Plik obrazu jest niekompletny lub uszkodzony.");
        }

        var orientation = (int)codec.EncodedOrigin;
        var swapsDimensions = orientation is >= 5 and <= 8;

        var orientedWidth = swapsDimensions ? height : width;
        var orientedHeight = swapsDimensions ? width : height;

        var scale = Math.Min(
            1d,
            (double)MaxOutputDimension /
            Math.Max(orientedWidth, orientedHeight));

        var outputWidth = Math.Max(
            1,
            (int)Math.Round(orientedWidth * scale));

        var outputHeight = Math.Max(
            1,
            (int)Math.Round(orientedHeight * scale));

        using var output = new SKBitmap(
            outputWidth,
            outputHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque);

        using (var canvas = new SKCanvas(output))
        {
            // Przezroczystość PNG zastępujemy białym tłem.
            canvas.Clear(SKColors.White);

            canvas.Scale(
                (float)outputWidth / orientedWidth,
                (float)outputHeight / orientedHeight);

            var matrix = GetOrientationMatrix(
                orientation,
                width,
                height);

            canvas.Concat(ref matrix);
            canvas.DrawBitmap(decoded, 0, 0);
            canvas.Flush();
        }

        // Nowy obraz powstaje wyłącznie z pikseli.
        // Nie kopiujemy EXIF, GPS ani innych metadanych wejściowych.
        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Jpeg,
            quality: 85);

        if (encoded is null)
        {
            throw new InvalidOperationException(
                "Nie udało się zakodować zdjęcia.");
        }

        if (encoded.Size > LocalPhotoStorage.MaxStoredFileSizeBytes)
        {
            throw new PhotoValidationException(
                "Przetworzone zdjęcie przekracza limit rozmiaru.");
        }

        return new ProcessedPhoto(
            encoded.ToArray(),
            "image/jpeg",
            outputWidth,
            outputHeight);
    }

    private static SKMatrix GetOrientationMatrix(
        int orientation,
        int width,
        int height)
    {
        return orientation switch
        {
            2 => Matrix(-1, 0, width, 0, 1, 0),
            3 => Matrix(-1, 0, width, 0, -1, height),
            4 => Matrix(1, 0, 0, 0, -1, height),
            5 => Matrix(0, 1, 0, 1, 0, 0),
            6 => Matrix(0, -1, height, 1, 0, 0),
            7 => Matrix(0, -1, height, -1, 0, width),
            8 => Matrix(0, 1, 0, -1, 0, width),
            _ => SKMatrix.Identity
        };
    }

    private static SKMatrix Matrix(
        float scaleX,
        float skewX,
        float transX,
        float skewY,
        float scaleY,
        float transY)
    {
        return new SKMatrix
        {
            ScaleX = scaleX,
            SkewX = skewX,
            TransX = transX,
            SkewY = skewY,
            ScaleY = scaleY,
            TransY = transY,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };
    }
}