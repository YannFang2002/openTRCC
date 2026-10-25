using OpenTRCC.Core.Imaging;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class ImageTransformsTests
{
    [Fact]
    public void Rotate90_Swaps_Dimensions_And_Moves_TopLeft_To_TopRight()
    {
        var source = new BgraImage(2, 3);
        source.SetPixel(0, 0, 0, 0, 255); // red at top-left
        source.SetPixel(1, 0, 0, 255, 0); // green at top-right

        BgraImage rotated = ImageTransforms.RotateClockwise(source, 90);

        Assert.Equal(3, rotated.Width);
        Assert.Equal(2, rotated.Height);
        Assert.Equal((byte)0, rotated.GetPixel(2, 0).G);
        Assert.Equal((byte)255, rotated.GetPixel(2, 0).R); // red moved to (h-1, 0)
        Assert.Equal((byte)255, rotated.GetPixel(2, 1).G); // green moved to (h-1, 1)
    }

    [Fact]
    public void Rotate270_Puts_TopLeft_At_BottomLeft()
    {
        var source = new BgraImage(2, 3);
        source.SetPixel(0, 0, 0, 0, 255);

        BgraImage rotated = ImageTransforms.RotateClockwise(source, 270);

        Assert.Equal(3, rotated.Width);
        Assert.Equal(2, rotated.Height);
        Assert.Equal((byte)255, rotated.GetPixel(0, 1).R);
    }

    [Fact]
    public void Rotate180_Is_Two_Nineties()
    {
        var source = new BgraImage(4, 5);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                source.SetPixel(x, y, (byte)(x * 10), (byte)(y * 10), 0);
            }
        }

        BgraImage twice = ImageTransforms.RotateClockwise(ImageTransforms.RotateClockwise(source, 90), 90);
        BgraImage direct = ImageTransforms.RotateClockwise(source, 180);

        Assert.Equal(source.Width, direct.Width);
        Assert.Equal(source.Height, direct.Height);
        Assert.Equal(direct.Pixels, twice.Pixels);
        Assert.Equal(source.Pixels[..4], direct.Pixels[^4..]); // top-left lands at bottom-right
    }

    [Fact]
    public void Three_Hundred_And_Twenty_By_Two_Forty_Becomes_Line_Order_240x320()
    {
        var canvas = new BgraImage(320, 240);
        canvas.SetPixel(0, 0, 0, 0, 255); // top-left red

        BgraImage line = ImageTransforms.RotateClockwise(canvas, 90);

        Assert.Equal(240, line.Width);
        Assert.Equal(320, line.Height);
        Assert.Equal((byte)255, line.GetPixel(239, 0).R);
    }

    [Theory]
    [InlineData(FlipMode.Horizontal)]
    [InlineData(FlipMode.Vertical)]
    [InlineData(FlipMode.Both)]
    public void Flip_Preserves_Dimensions(FlipMode mode)
    {
        var source = new BgraImage(7, 3);
        BgraImage flipped = ImageTransforms.Flip(source, mode);

        Assert.Equal(7, flipped.Width);
        Assert.Equal(3, flipped.Height);
    }

    [Fact]
    public void Rejects_Invalid_Rotation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageTransforms.RotateClockwise(new BgraImage(2, 2), 45));
    }
}
