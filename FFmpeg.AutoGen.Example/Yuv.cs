namespace FFmpeg.AutoGen.Example
{
    public struct Yuv
    {
        private double _y;
        private double _u;
        private double _v;

        public Yuv(double y, double u, double v)
        {
            this._y = y;
            this._u = u;
            this._v = v;
        }

        public double Y
        {
            get { return this._y; }
            set { this._y = value; }
        }

        public double U
        {
            get { return this._u; }
            set { this._u = value; }
        }

        public double V
        {
            get { return this._v; }
            set { this._v = value; }
        }

        public bool Equals(Yuv yuv)
        {
            return (this.Y == yuv.Y) && (this.U == yuv.U) && (this.V == yuv.V);
        }
    }
}