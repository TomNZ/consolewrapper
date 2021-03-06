using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading;

namespace ConsoleWrapper
{
    class LineSprite : Renderable, IDisposable, IAnimatable
    {
        public static readonly int MaxLineWidth = 80;

        private Texture _lineTexture;
        public Texture LineTexture
        {
            get { return _lineTexture; }
        }
        private Material _lineMaterial;
        public Material LineMaterial
        {
            get { return _lineMaterial; }
        }

        private ConsoleString _line;
        private String _fontFace;
        private int _fontSize;
        private FontTexture _fontTexture;

        private int _lineWidth;
        public override int Width
        {
            get { return _lineWidth; }
        }
        private int _lineHeight;
        public override int Height
        {
            get { return _lineHeight; }
        }

        // Allows for line invalidation to rebuild with the D3D device
        private bool _valid = false;
        private bool _building = false;

        // Animation for expanding lines
        private double _expandTime = 0.5f; // Time in seconds - set to 0 to disable
        private double _liveTime = 0;
        private double _widthFactor = 1;

        public LineSprite(ConsoleString line, String fontFace, int fontSize, Device device)
        {
            _line = line;
            _fontFace = fontFace;
            _fontSize = fontSize;

            Rebuild(device);
        }

        public LineSprite(ConsoleString line, String fontFace, int fontSize, Device device, bool deviceValid)
        {
            _line = line;
            _fontFace = fontFace;
            _fontSize = fontSize;

            if (deviceValid)
                Rebuild(device);
        }

        private string _displayString;

        public override void Invalidate()
        {
            _valid = false;
        }

        // Rebuilds the object if it happens to get lost eg on a device reset
        public override void Rebuild(Device device)
        {
            if (_valid || _building)
                return;

            _building = true;

            // Teardown
            this.Dispose();

            // Set up the line texture
            Bitmap b = new Bitmap(1, 1);
            System.Drawing.Font font = new System.Drawing.Font(_fontFace, _fontSize);

            Graphics g = Graphics.FromImage(b);

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            _displayString = "";
            string tempString = _line.Text;

            while (tempString.Length > MaxLineWidth)
            {
                _displayString += tempString.Substring(0, MaxLineWidth) + Environment.NewLine;
                tempString = tempString.Substring(MaxLineWidth);
            }
            _displayString += tempString;

            SizeF stringSize = g.MeasureString(_displayString, font, new PointF(0, 0), StringFormat.GenericTypographic);

            _lineWidth = Size.Truncate(stringSize).Width;
            _lineHeight = Size.Truncate(stringSize).Height;

            ThreadPool.QueueUserWorkItem(new WaitCallback(RebuildAsync), device);
        }

        private void RebuildAsync(Object deviceObj)
        {
            try
            {
                Device device = (Device)deviceObj;

                // Rebuild
                if (_line.Equals(""))
                {
                    _valid = true;
                }
                else
                {
                    try
                    {
                        int firstTick = Environment.TickCount;

                        // Get the font
                        _fontTexture = FontTextureFactory.GetFontTexture(_fontFace, _fontSize, device);

                        // Set up the material
                        _lineMaterial = new Material();
                        _lineMaterial.Diffuse = _line.Color;

                        _valid = true;
                    }
                    catch (Exception)
                    {
                        _valid = false;
                        _lineTexture = null;
                        return;
                    }
                }
            }
            catch (Exception)
            {
                _valid = false;
                _lineTexture = null;
            }
            finally
            {
                Animate(0);
                _building = false;
            }
        }

        public void Animate(double time)
        {
            _liveTime += time;

            if (_liveTime > _expandTime)
            {
                _widthFactor = 1;
            }
            else
            {
                // Gompertz curve
                double a, b, c, t;

                a = 1;
                b = -0.5;
                c = -1;
                t = ((_liveTime) / _expandTime) * 6;

                _widthFactor = a * Math.Exp(b * Math.Exp(c * t));
            }
        }

        public override void Draw(Device device)
        {
            if (!_valid && !_building)
                Rebuild(device);

            if (!_valid || _building)
                return;

            if (_fontTexture.Valid)
            {
                // Set up the expansion
                Matrix worldBackup = device.Transform.World;

                Mesh letterSprite = WrapperGraphics.LetterSprite;

                if (letterSprite == null)
                    return;

                CustomVertex.PositionNormalTextured[] verts = new CustomVertex.PositionNormalTextured[4];

                verts[0].Position = new Vector3(0, 0, -_fontTexture.LetterHeight);
                verts[0].Normal = new Vector3(0, 1, 0);

                verts[1].Position = new Vector3(_fontTexture.LetterWidth, 0, -_fontTexture.LetterHeight);
                verts[1].Normal = new Vector3(0, 1, 0);

                verts[2].Position = new Vector3(_fontTexture.LetterWidth, 0, 0);
                verts[2].Normal = new Vector3(0, 1, 0);

                verts[3].Position = new Vector3(0, 0, 0);
                verts[3].Normal = new Vector3(0, 1, 0);

                AttributeRange[] attributes = new AttributeRange[1];
                attributes[0].AttributeId = 0;
                attributes[0].FaceCount = 2;
                attributes[0].FaceStart = 0;
                attributes[0].VertexCount = 4;
                attributes[0].VertexStart = 0;

                short[] indices = new short[]
                        {
                            0, 1, 2,
                            0, 2, 3
                        };

                char[] letters = _displayString.ToCharArray();

                // Set the material and texture
                device.Material = _lineMaterial;
                device.SetTexture(0, _fontTexture.Texture);

                letterSprite.SetIndexBufferData(indices, LockFlags.Discard);
                letterSprite.SetAttributeTable(attributes);

                int numLines = 0;
                int letterPos = 0;

                foreach (char c in letters)
                {
                    if (c == '\n')
                    {
                        numLines++;
                        letterPos = 0;
                        device.Transform.World = worldBackup;
                        device.Transform.World *= Matrix.Translation(0, 0, -(float)(_fontTexture.LetterHeight * 1) * numLines);
                    }
                    else if (c == '\r')
                    {
                    }
                    else
                    {
                        FontTexture.LetterInfo letter = _fontTexture.Letter(c);

                        verts[0].Tu = (float)letter.ul; verts[0].Tv = (float)letter.vb;
                        verts[1].Tu = (float)letter.ur; verts[1].Tv = (float)letter.vb;
                        verts[2].Tu = (float)letter.ur; verts[2].Tv = (float)letter.vt;
                        verts[3].Tu = (float)letter.ul; verts[3].Tv = (float)letter.vt;

                        // Initial code for some extra letter animation
                        // Disabled for now as it's rubbish
                        //if (_widthFactor != 1)
                        //{
                        //    float heightDiff = Math.Min(Math.Max(((double)((double)letterPos + 40 / (double)LineSprite.MaxLineWidth) * _widthFactor), 0.0), 1.0) * (double)_fontTexture.LetterHeight / 2.0;
                        //    verts[0].Position = new Vector3(0, 0, -_fontTexture.LetterHeight + heightDiff);
                        //    verts[1].Position = new Vector3(_fontTexture.LetterWidth, 0, -_fontTexture.LetterHeight + heightDiff);
                        //    verts[2].Position = new Vector3(_fontTexture.LetterWidth, 0, -heightDiff);
                        //    verts[3].Position = new Vector3(0, 0, -heightDiff);
                        //}

                        letterSprite.SetVertexBufferData(verts, LockFlags.Discard);

                        // Animation translation
                        if (_widthFactor != 1)
                        {
                            device.Transform.World *= Matrix.Scaling(new Vector3((float)_widthFactor, 1, 1));
                        }

                        try
                        {
                            letterSprite.DrawSubset(0);
                        }
                        catch (Exception)
                        {
                            _valid = false;
                        }

                        // Undo animation for positioning next letter
                        if (_widthFactor != 1)
                        {
                            device.Transform.World *= Matrix.Scaling(new Vector3(1 / (float)_widthFactor, 1, 1));
                        }

                        device.Transform.World *= Matrix.Translation((float)letter.w, 0F, 0F);

                        letterPos++;
                    }
                }

                // Restore the world matrix
                device.Transform.World = worldBackup;
            }
        }

        #region IDisposable Members

        public override void Dispose()
        {
            if (_lineTexture != null)
            {
                _lineTexture.Dispose();
                _lineTexture = null;
            }
			_fontTexture = null;
        }

        #endregion
    }
}
