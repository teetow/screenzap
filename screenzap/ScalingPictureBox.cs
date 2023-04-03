using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ScalingPictureBox : PictureBox
    {
        public InterpolationMode InterpolationMode { get; set; }
        public ScalingPictureBox()
        {
            this.SizeMode = PictureBoxSizeMode.Zoom;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            InitializeComponent();
        }
        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.InterpolationMode = InterpolationMode;
            if (InterpolationMode == InterpolationMode.NearestNeighbor)
            {
                eventArgs.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            }

            base.OnPaint(eventArgs);
        }

    }
}
