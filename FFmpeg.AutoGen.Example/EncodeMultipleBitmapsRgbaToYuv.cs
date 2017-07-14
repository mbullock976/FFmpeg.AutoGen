using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFormat = System.Windows.Media.PixelFormat;
using Point = System.Windows.Point;

namespace FFmpeg.AutoGen.Example
{
    public class EncodeMultipleBitmapsRgbaToYuv
    {
        private const int EAGAIN = 11;
        private const int AVERROR_EAGAIN = -EAGAIN;
        private const int AVERROR_EOF = -0x20464F45;
        
        private readonly List<BitmapSource> _decodedBitmaps = new List<BitmapSource>();

        public EncodeMultipleBitmapsRgbaToYuv()
        {
            LoadBitmapsFromDisk();
        }

        private void LoadBitmapsFromDisk()
        {
            for (int i = 0; i < 1439; i++)
            {
                BitmapSource bs =
                    new BitmapImage(new Uri(
                        $@"C:\RgbaToH264\FFmpeg.AutoGen.Example\bin\x64\Debug\frame.buffer{i}.jpg"));
                _decodedBitmaps.Add(bs);
            }
        }

        public unsafe void video_encode_example(
            string outputFilename,
            int codec_id)
        {

            // register path to ffmpeg
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                    var ffmpegPath = $@"../../../../FFmpeg/bin/{(Environment.Is64BitProcess ? @"x64" : @"x86")}";
                    InteropHelper.RegisterLibrariesSearchPath(ffmpegPath);
                    break;
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    var libraryPath = Environment.GetEnvironmentVariable(InteropHelper.LD_LIBRARY_PATH);
                    InteropHelper.RegisterLibrariesSearchPath(libraryPath);
                    break;
            }

            ffmpeg.av_register_all();
            ffmpeg.avcodec_register_all();
            ffmpeg.avformat_network_init();

            Console.WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");

            AVCodec* codec;
            AVCodecContext* c = null;
            int i, ret, x, y, got_output;
            //FILE* f;
            AVFrame* frame;
            AVPacket* pkt;
            //uint8_t endcode[] = { 0, 0, 1, 0xb7 };
            var endcode = new byte[4];
            endcode[0] = 0;
            endcode[1] = 0;
            endcode[2] = 1;
            endcode[3] = 0xb7;


            //find the h264 video encoder
            codec = ffmpeg.avcodec_find_encoder((AVCodecID)codec_id);
            if (codec == null)
                throw new ApplicationException(@"Unsupported codec");

            c = ffmpeg.avcodec_alloc_context3(codec);
            if (c == null)
                throw new ApplicationException("Could not allocate video codec context\n");

            /* put sample parameters */
            c->bit_rate = 400000;
            /* resolution must be a multiple of two */
            c->width = 640;
            c->height = 360;            

            /* frames per second */
            c->time_base = new AVRational { num = 1, den = 25 };


            /* emit one intra frame every ten frames 
             * check frame pict_type before passing frame 
             * to encoder, if frame->pict_type is AV_PICTURE_TYPE_I 
             * then gop_size is ignored and the output of encoder 
             * will always be I frame irrespective to gop_size 
             */
            c->gop_size = 10;
            c->max_b_frames = 1;
            c->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV422P;

            if ((AVCodecID)codec_id == AVCodecID.AV_CODEC_ID_H264)
                ffmpeg.av_opt_set(c->priv_data, "preset", "slow", 0);

            /* open it */
            if (ffmpeg.avcodec_open2(c, codec, null) < 0)
                throw new ApplicationException("Could not open codec\n");

            if (File.Exists(outputFilename))
                File.Delete(outputFilename);

            var fileStream = File.Open(outputFilename, FileMode.OpenOrCreate);
            if (fileStream == null)
                throw new ApplicationException($"Could not open {outputFilename}\n");

            frame = ffmpeg.av_frame_alloc();
            if (frame == null)
                throw new ApplicationException($"Could not allocate video frame\n");

            frame->format = (int)c->pix_fmt;
            frame->width = c->width;
            frame->height = c->height;

            ret = ffmpeg.av_frame_get_buffer(frame, 32);
            if (ret < 0)
                throw new ApplicationException("Could not allocate the video frame data\n");

            var dstData = new byte_ptrArray4();
            dstData[0] = frame->data[0];
            dstData[1] = frame->data[1];
            dstData[2] = frame->data[2];
            dstData[3] = frame->data[3];

            var dstLineSize = new int_array4();
            dstLineSize[0] = frame->linesize[0];
            dstLineSize[1] = frame->linesize[1];
            dstLineSize[2] = frame->linesize[2];
            dstLineSize[3] = frame->linesize[3];

            /* the image can be allocated by any means and av_image_alloc() is 
            * just the most convenient way if av_malloc() is to be used */
            ret = ffmpeg.av_image_alloc(
                ref dstData,
                ref dstLineSize,
                c->width,
                c->height,
                c->pix_fmt, 32);

            if (ret < 0)
                throw new ApplicationException("Could not allocate raw picture buffer\n");

            // 
            // RGB to YUV: 
            //    http://stackoverflow.com/questions/16667687/how-to-convert-rgb-from-yuv420p-for-ffmpeg-encoder 
            // 
            // Create some dummy RGB "frame" 
            byte* unmanagedRgba32Array = stackalloc byte[4 * c->width * c->height];


            var ctx = ffmpeg.sws_getContext(
                c->width,
                c->height,
                AVPixelFormat.AV_PIX_FMT_RGBA,
                c->width,
                c->height,
                AVPixelFormat.AV_PIX_FMT_YUV422P,
                ffmpeg.SWS_LANCZOS | ffmpeg.SWS_ACCURATE_RND,
                null,
                null,
                null);


            pkt = ffmpeg.av_packet_alloc();

            var nextframe = -1;

            /* encode 1 second of video */
            for (i = 0; i < 1438; i++)
            {
                ffmpeg.av_init_packet(pkt);
                pkt->data = null; // packet data will be allocated by the encoder 
                pkt->size = 0;

                fileStream.Flush();              

                var managedRgba32Array = ProcessBitmap(i);

                int size = Marshal.SizeOf(managedRgba32Array[0]) * managedRgba32Array.Length;
                IntPtr pnt = Marshal.AllocHGlobal(size);
                Marshal.Copy(managedRgba32Array, 0, pnt, managedRgba32Array.Length);

                unmanagedRgba32Array = (byte*)pnt;

                var inData = new byte*[1];
                inData[0] = unmanagedRgba32Array;


                // NOTE: In a more general setting, the rows of your input image may 
                //       be padded; that is, the bytes per row may not be 4 * width. 
                //       In such cases, inLineSize should be set to that padded width. 
                // 
                //int inLinesize[1] = { 4 * c->width }; // RGBA stride
                var inLineSize = new int[1];
                inLineSize[0] = 4 * c->width;

                ffmpeg.sws_scale(ctx, inData, inLineSize, 0, c->height, frame->data, frame->linesize);

                Marshal.FreeHGlobal(pnt);

                frame->pts = i;

                /* encode the image */
                Encode(c, frame, pkt, fileStream);
            }

            /* flush the encoder */
            Encode(c, null, pkt, fileStream);

            /* add sequence end code to have a real MPEG file */
            fileStream.Write(endcode, 0, endcode.Length);
            fileStream.Close();

            ffmpeg.avcodec_free_context(&c);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&pkt);
        }


        private static unsafe void Encode(AVCodecContext* ctx, AVFrame* frame, AVPacket* pkt, FileStream outfile)
        {
            var ret = ffmpeg.avcodec_send_frame(ctx, frame);
            if (ret < 0)
                throw new ApplicationException("Error sending frame for encoding\n");

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_packet(ctx, pkt);

                if (ret == AVERROR_EAGAIN || ret == AVERROR_EOF)
                    break;
                if (ret < 0)
                    throw new ApplicationException("Error during encoding\n");

                if (pkt->size <= 0)
                    Console.WriteLine($"Skipping empty packet for stream {pkt->stream_index}.");

                var size = pkt->size; // first byte is size;
                var target = new byte[size];
                for (var z = 0; z < size; ++z)
                    target[z] = pkt->data[z + 1];

                outfile.Write(target, 0, size);
                ffmpeg.av_packet_unref(pkt);
            }
        }

        private byte[] ProcessBitmap(int frameNumber)
        {
            var wb = new WriteableBitmap(_decodedBitmaps[frameNumber]);

            ApplyMarkup(ref wb);

            var rgba32Data = ConvertToRgba32(ref wb);

            //var rgb24Data = ConvertToRgba24(_decodedBitmaps[frameNumber]);
            //return rgb24Data;

            return rgba32Data;
        }

        private void ApplyMarkup(ref WriteableBitmap wb)
        {
            var sb = new StringBuilder();
            sb.Append("Frame Diagnostics");
            sb.AppendLine();
            sb.Append("----------------------");
            sb.AppendLine();
            sb.Append(DateTime.Now.ToString("HH:mm:ss:fff"));


            var frameSupplementText = new FormattedText(sb.ToString(),
                new CultureInfo("en-us"),
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                10,
                new SolidColorBrush(Colors.White), 1.25);

            var frameDiagnosticsTextWidth = (int)frameSupplementText.Width;
            var frameDiagnosticsTextHeight = (int)frameSupplementText.Height;
            var backgroundBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 255)) { Opacity = 0.5 };


            var frameDiagBackgroundRect = new Rect(
                new Point(0, 0),
                new System.Windows.Size(
                    frameDiagnosticsTextWidth + 65,
                    frameDiagnosticsTextHeight + 100));

            var frameDiagVisual = new DrawingVisual();            
            var context = frameDiagVisual.RenderOpen();
            context.DrawRectangle(backgroundBrush, null, frameDiagBackgroundRect);
            context.DrawText(frameSupplementText, new Point(5, 5));
            context.Close();

            var frameDiagRenderTargetBitmap = new RenderTargetBitmap(
                (int)frameDiagBackgroundRect.Width,
                (int)frameDiagBackgroundRect.Height,
                96,
                96,
                PixelFormats.Pbgra32);

            frameDiagRenderTargetBitmap.Render(frameDiagVisual);

            wb.Blit(new Rect(10, 10, 150, 130), new WriteableBitmap(frameDiagRenderTargetBitmap),
                new Rect(0, 0, 100, 130));

        }

        private byte[] ConvertToRgba32(ref WriteableBitmap wb)
        {
            Bitmap bmp = BitmapFromWriteableBitmap(wb);

            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                    bmp.PixelFormat);

            // Get the address of the first line.
            IntPtr ptr = bmpData.Scan0;

            // Declare an array to hold the bytes of the bitmap.
            int bytes = bmpData.Stride * bmp.Height;
            byte[] rgbValues = new byte[bytes];

            // Copy the RGB values into the array.
            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);

            // unlock the bitmap bufer
            bmp.UnlockBits(bmpData);

            return rgbValues;
        }

        private byte[] ConvertToRgba24(BitmapSource source)
        {
            Bitmap bmp = GetBitmap(source);

            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                    bmp.PixelFormat);

            // Get the address of the first line.
            IntPtr ptr = bmpData.Scan0;

            // Declare an array to hold the bytes of the bitmap.
            int bytes = bmpData.Stride * bmp.Height;
            byte[] rgbValues = new byte[bytes];

            // Copy the RGB values into the array.
            System.Runtime.InteropServices.Marshal.Copy(ptr, rgbValues, 0, bytes);

            // unlock the bitmap bufer
            bmp.UnlockBits(bmpData);

            return rgbValues;
        }

        Bitmap GetBitmap(BitmapSource source)
        {
            Bitmap bmp = new Bitmap(
                source.PixelWidth,
                source.PixelHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(
                new Rectangle(System.Drawing.Point.Empty, bmp.Size),
                ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            source.CopyPixels(
                Int32Rect.Empty,
                data.Scan0,
                data.Height * data.Stride,
                data.Stride);
            bmp.UnlockBits(data);
            return bmp;
        }

        private System.Drawing.Bitmap BitmapFromWriteableBitmap(WriteableBitmap writeBmp)
        {
            System.Drawing.Bitmap bmp;
            using (MemoryStream outStream = new MemoryStream())
            {
                BmpBitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create((BitmapSource)writeBmp));
                enc.Save(outStream);
                bmp = new System.Drawing.Bitmap(outStream);
            }
            return bmp;
        }

        public static Yuv RgbtoYuv(Rgb rgb)
        {
            double y = rgb.R * .299000 + rgb.G * .587000 + rgb.B * .114000;
            double u = rgb.R * -.168736 + rgb.G * -.331264 + rgb.B * .500000 + 128;
            double v = rgb.R * .500000 + rgb.G * -.418688 + rgb.B * -.081312 + 128;

            return new Yuv(y, u, v);
        }
    }
}