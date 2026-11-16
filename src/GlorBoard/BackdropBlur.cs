using System;

namespace GlorBoard;

internal sealed class BackdropBlur
{
    private const int MaxWidth = 624;
    private const int MaxHeight = 1080;
    private int[] pixels = [];
    private int[] intermediate = [];

    internal static (int Width, int Height) SampleSize(int width, int height)
    {
        double scale = Math.Min(1, Math.Min(MaxWidth / (double)width, MaxHeight / (double)height));
        return (Math.Max(2, (int)Math.Round(width * scale)), Math.Max(2, (int)Math.Round(height * scale)));
    }

    internal unsafe int[] Apply(int* source, int sourceWidth, int sourceHeight, int width, int height, int radius)
    {
        int length = checked(width * height);
        if (pixels.Length != length)
        {
            pixels = new int[length];
            intermediate = new int[length];
        }

        for (int y = 0; y < height; y++)
        {
            int row = Math.Min((int)((y + .5) * sourceHeight / height), sourceHeight - 1) * sourceWidth;
            for (int x = 0; x < width; x++)
            {
                int column = Math.Min((int)((x + .5) * sourceWidth / width), sourceWidth - 1);
                pixels[y * width + x] = source[row + column];
            }
        }

        for (int y = 0; y < height; y++)
            AverageLine(pixels, intermediate, y * width, 1, width, radius);
        for (int x = 0; x < width; x++)
            AverageLine(intermediate, pixels, x, width, height, radius);
        return pixels;
    }

    private static void AverageLine(int[] input, int[] output, int offset, int stride, int length, int radius)
    {
        int red = 0, green = 0, blue = 0;
        for (int i = -radius; i <= radius; i++)
        {
            int pixel = input[offset + Math.Clamp(i, 0, length - 1) * stride];
            red += pixel >> 16 & 255;
            green += pixel >> 8 & 255;
            blue += pixel & 255;
        }

        int count = radius * 2 + 1;
        for (int i = 0; i < length; i++)
        {
            output[offset + i * stride] = (red / count << 16) | (green / count << 8) | (blue / count);
            int leaving = input[offset + Math.Max(0, i - radius) * stride];
            int entering = input[offset + Math.Min(length - 1, i + radius + 1) * stride];
            red += (entering >> 16 & 255) - (leaving >> 16 & 255);
            green += (entering >> 8 & 255) - (leaving >> 8 & 255);
            blue += (entering & 255) - (leaving & 255);
        }
    }

    internal void Release()
    {
        pixels = [];
        intermediate = [];
    }
}
