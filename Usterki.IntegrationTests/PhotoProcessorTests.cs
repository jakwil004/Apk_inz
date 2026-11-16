using SkiaSharp;
using Usterki.Infrastructure.Photos;
using Xunit;

namespace Usterki.IntegrationTests;

public sealed class PhotoProcessorTests
{
    [Fact]
    public void Process_ValidPng_ReturnsJpeg()
    {
        using var bitmap = new SKBitmap(40, 20);
        bitmap.Erase(SKColors.Red);

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        var processor = new PhotoProcessor();

        var result = processor.Process(png.ToArray());

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(40, result.Width);
        Assert.Equal(20, result.Height);

        using var data = SKData.CreateCopy(result.Content);
        using var codec = SKCodec.Create(data);

        Assert.NotNull(codec);
        Assert.Equal(
            SKEncodedImageFormat.Jpeg,
            codec!.EncodedFormat);
    }

    [Fact]
    public void Process_TextFile_ThrowsValidationException()
    {
        var processor = new PhotoProcessor();

        var content = System.Text.Encoding.UTF8.GetBytes(
            "To nie jest zdjęcie.");

        Assert.Throws<PhotoValidationException>(
            () => processor.Process(content));
    }

    [Fact]
    public void Process_OversizedFile_ThrowsValidationException()
    {
        var processor = new PhotoProcessor();

        var content = new byte[
            PhotoProcessor.MaxUploadBytes + 1];

        Assert.Throws<PhotoValidationException>(
            () => processor.Process(content));
    }
}