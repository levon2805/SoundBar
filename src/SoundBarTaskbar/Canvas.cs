namespace SoundBarTaskbar
{
    /// <summary>
    /// A see-through drawing surface for the indicator. Pixels are stored premultiplied (BGRA), which is what
    /// Windows needs to blend a window over the taskbar.
    ///
    /// GDI can't draw see-through text, so text is drawn white-on-black on a second surface. How bright each
    /// pixel is there tells us how much of the text covers it, and the text is then blended in with its colour.
    /// The surfaces are reused and only remade when the size changes, so drawing allocates nothing.
    /// </summary>
    internal sealed unsafe class Canvas : IDisposable
    {
        private IntPtr _dc, _bitmap, _oldBitmap;
        private uint* _pixels;
        private IntPtr _maskDc, _maskBitmap, _oldMaskBitmap;
        private uint* _mask;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>The device context the masks are drawn on, for measuring text with a font selected.</summary>
        public IntPtr MeasureDc => _maskDc;

        public Canvas()
        {
            _dc = Native.CreateCompatibleDC(IntPtr.Zero);
            _maskDc = Native.CreateCompatibleDC(IntPtr.Zero);
            Native.SetBkMode(_maskDc, Native.TRANSPARENT);
            Native.SetTextColor(_maskDc, 0x00FFFFFF);
        }

        /// <summary>Makes the surface this size and clears it to "almost see-through" so the whole area takes clicks.</summary>
        public void Begin(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (width != Width || height != Height)
            {
                FreeBitmaps();
                _bitmap = CreateSurface(_dc, width, height, out _pixels);
                _oldBitmap = Native.SelectObject(_dc, _bitmap);
                _maskBitmap = CreateSurface(_maskDc, width, height, out _mask);
                _oldMaskBitmap = Native.SelectObject(_maskDc, _maskBitmap);
                Width = width;
                Height = height;
            }

            // Alpha 1 of 255: invisible, but Windows still sends us the mouse. Fully clear pixels would let
            // clicks fall through to the taskbar.
            new Span<uint>(_pixels, width * height).Fill(0x01000000);
        }

        private static IntPtr CreateSurface(IntPtr dc, int width, int height, out uint* pixels)
        {
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // Top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Native.BI_RGB,
            };
            IntPtr bitmap = Native.CreateDIBSection(dc, ref header, Native.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new InvalidOperationException("Couldn't create the drawing surface.");
            pixels = (uint*)bits;
            return bitmap;
        }

        /// <summary>Fills a rounded rectangle with smooth corners. Colour is 0xAARRGGBB (not premultiplied).</summary>
        public void FillRoundedRect(int left, int top, int right, int bottom, float radius, uint color)
        {
            left = Math.Max(0, left); top = Math.Max(0, top);
            right = Math.Min(Width, right); bottom = Math.Min(Height, bottom);
            if (right <= left || bottom <= top) return;

            float r = Math.Min(radius, Math.Min(right - left, bottom - top) / 2f);
            for (int y = top; y < bottom; y++)
            {
                float py = y + 0.5f;
                for (int x = left; x < right; x++)
                {
                    float px = x + 0.5f;
                    // Distance into the nearest corner's circle; 1 = fully inside
                    float cx = px < left + r ? left + r : px > right - r ? right - r : px;
                    float cy = py < top + r ? top + r : py > bottom - r ? bottom - r : py;
                    float dx = px - cx, dy = py - cy;
                    float coverage = r <= 0 ? 1f : Math.Clamp(r - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0f, 1f);
                    if (coverage > 0) Blend(ref _pixels[y * Width + x], color, coverage);
                }
            }
        }

        /// <summary>Measures text in the given font.</summary>
        public Native.SIZE Measure(string text, IntPtr font)
        {
            IntPtr old = Native.SelectObject(_maskDc, font);
            Native.GetTextExtentPoint32W(_maskDc, text, text.Length, out var size);
            Native.SelectObject(_maskDc, old);
            return size;
        }

        /// <summary>Draws text with its top-left corner at (x, y). Colour is 0xAARRGGBB (not premultiplied).</summary>
        public void DrawText(string text, IntPtr font, int x, int y, uint color)
        {
            if (string.IsNullOrEmpty(text)) return;
            var size = Measure(text, font);
            int left = Math.Max(0, x), top = Math.Max(0, y);
            int right = Math.Min(Width, x + size.Width + 2), bottom = Math.Min(Height, y + size.Height);
            if (right <= left || bottom <= top) return;

            // Clear just the area the text covers, draw it in white, then use its brightness as coverage
            for (int row = top; row < bottom; row++)
                new Span<uint>(_mask + row * Width + left, right - left).Clear();

            IntPtr old = Native.SelectObject(_maskDc, font);
            Native.TextOutW(_maskDc, x, y, text, text.Length);
            Native.SelectObject(_maskDc, old);
            Native.GdiFlush();

            for (int row = top; row < bottom; row++)
            {
                uint* maskRow = _mask + row * Width;
                uint* pixelRow = _pixels + row * Width;
                for (int col = left; col < right; col++)
                {
                    uint m = maskRow[col];
                    // Greyscale smoothing gives equal channels; take the brightest to be safe
                    uint level = Math.Max(m & 0xFF, Math.Max((m >> 8) & 0xFF, (m >> 16) & 0xFF));
                    if (level != 0) Blend(ref pixelRow[col], color, level / 255f);
                }
            }
        }

        private static void Blend(ref uint destination, uint color, float coverage)
        {
            float a = ((color >> 24) & 0xFF) / 255f * coverage;
            if (a <= 0) return;
            float r = ((color >> 16) & 0xFF) * a, g = ((color >> 8) & 0xFF) * a, b = (color & 0xFF) * a;

            uint d = destination;
            float keep = 1f - a;
            uint outA = (uint)Math.Min(255f, a * 255f + ((d >> 24) & 0xFF) * keep + 0.5f);
            uint outR = (uint)Math.Min(255f, r + ((d >> 16) & 0xFF) * keep + 0.5f);
            uint outG = (uint)Math.Min(255f, g + ((d >> 8) & 0xFF) * keep + 0.5f);
            uint outB = (uint)Math.Min(255f, b + (d & 0xFF) * keep + 0.5f);
            destination = (outA << 24) | (outR << 16) | (outG << 8) | outB;
        }

        /// <summary>Puts the finished picture on the window.</summary>
        public void Present(IntPtr window)
        {
            var size = new Native.SIZE { Width = Width, Height = Height };
            var source = new Native.POINT();
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            // No destination point: a child window is placed with SetWindowPos instead
            Native.UpdateLayeredWindow(window, IntPtr.Zero, IntPtr.Zero, ref size, _dc, ref source, 0, ref blend, Native.ULW_ALPHA);
        }

        private void FreeBitmaps()
        {
            if (_bitmap != IntPtr.Zero)
            {
                Native.SelectObject(_dc, _oldBitmap);
                Native.DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
                _pixels = null;
            }
            if (_maskBitmap != IntPtr.Zero)
            {
                Native.SelectObject(_maskDc, _oldMaskBitmap);
                Native.DeleteObject(_maskBitmap);
                _maskBitmap = IntPtr.Zero;
                _mask = null;
            }
            Width = Height = 0;
        }

        public void Dispose()
        {
            FreeBitmaps();
            if (_dc != IntPtr.Zero) { Native.DeleteDC(_dc); _dc = IntPtr.Zero; }
            if (_maskDc != IntPtr.Zero) { Native.DeleteDC(_maskDc); _maskDc = IntPtr.Zero; }
        }
    }
}
