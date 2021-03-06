﻿using System;
using System.IO;

namespace FFmpeg.AutoGen.Example
{
    public class EncodeSingleBitmap
    {
        /// <summary>
        ///     Taken from https://github.com/codefromabove/FFmpegRGBAToYUV/blob/master/FFmpegRGBAToYUV/ConvertRGBA.cpp
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="codec_id"></param>
        public static unsafe void video_encode_example(
            string filename,
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
            codec = ffmpeg.avcodec_find_encoder((AVCodecID) codec_id);
            if (codec == null)
                throw new ApplicationException(@"Unsupported codec");

            c = ffmpeg.avcodec_alloc_context3(codec);
            if (c == null)
                throw new ApplicationException("Could not allocate video codec context\n");

            /* put sample parameters */
            c->bit_rate = 400000;
            /* resolution must be a multiple of two */
            c->width = 352;
            c->height = 288;
            /* frames per second */
            c->time_base = new AVRational {num = 1, den = 25};


            /* emit one intra frame every ten frames 
             * check frame pict_type before passing frame 
             * to encoder, if frame->pict_type is AV_PICTURE_TYPE_I 
             * then gop_size is ignored and the output of encoder 
             * will always be I frame irrespective to gop_size 
             */
            c->gop_size = 10;
            c->max_b_frames = 1;
            c->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

            if ((AVCodecID) codec_id == AVCodecID.AV_CODEC_ID_H264)
                ffmpeg.av_opt_set(c->priv_data, "preset", "slow", 0);

            /* open it */
            if (ffmpeg.avcodec_open2(c, codec, null) < 0)
                throw new ApplicationException("Could not open codec\n");

            if (File.Exists(filename))
                File.Delete(filename);

            var fileStream = File.Open(filename, FileMode.OpenOrCreate);
            if (fileStream == null)
                throw new ApplicationException($"Could not open {filename}\n");

            frame = ffmpeg.av_frame_alloc();
            if (frame == null)
                throw new ApplicationException($"Could not allocate video frame\n");

            frame->format = (int) c->pix_fmt;
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
            byte* rgba32Data = stackalloc byte[4 * c->width * c->height];

            var ctx = ffmpeg.sws_getContext(
                c->width,
                c->height,
                AVPixelFormat.AV_PIX_FMT_RGBA,
                c->width,
                c->height,
                AVPixelFormat.AV_PIX_FMT_YUV420P,
                0,
                (SwsFilter*) 0,
                (SwsFilter*) 0,
                (double*) 0);


            pkt = ffmpeg.av_packet_alloc();

            /* encode 1 second of video */
            for (i = 0; i < 25; i++)
            {
                ffmpeg.av_init_packet(pkt);
                pkt->data = null; // packet data will be allocated by the encoder 
                pkt->size = 0;

                fileStream.Flush();

                for (y = 0; y < c->height; y++)
                for (x = 0; x < c->width; x++)
                    frame->data[0][y * frame->linesize[0] + x] = (byte) (x + y + i * 3);

                /* Cb and Cr */
                for (y = 0; y < c->height / 2; y++)
                for (x = 0; x < c->width / 2; x++)
                {
                    frame->data[1][y * frame->linesize[1] + x] = (byte) (128 + y + i * 2);
                    frame->data[2][y * frame->linesize[2] + x] = (byte) (64 + x + i * 5);
                }

                var pos = rgba32Data;
                for (y = 0; y < c->height; y++)
                for (x = 0; x < c->width; x++)
                {
                    pos[0] = (byte) (i / (float) 25 * 255);
                    pos[1] = 0;
                    pos[2] = (byte) (x / (float) c->width * 255);
                    pos[3] = 255;
                    pos += 4;
                }

                //byte* inData = { rgba32Data }; // RGBA32 have one plane
                var inData = new byte*[1];
                inData[0] = rgba32Data;

                // 
                // NOTE: In a more general setting, the rows of your input image may 
                //       be padded; that is, the bytes per row may not be 4 * width. 
                //       In such cases, inLineSize should be set to that padded width. 
                // 
                //int inLinesize[1] = { 4 * c->width }; // RGBA stride
                var inLineSize = new int[1];
                inLineSize[0] = 4 * c->width;

                ffmpeg.sws_scale(ctx, inData, inLineSize, 0, c->height, frame->data, frame->linesize);

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

                //printf("Write packet %3"PRId64" (size=%5d)\n", pkt->pts, pkt->size);
                //fwrite(pkt->data, 1, pkt->size, outfile);

                if (pkt->size <= 0)
                    Console.WriteLine($"Skipping empty packet for stream {pkt->stream_index}.");

                var size = pkt->size; // first byte is size;
                var target = new byte[size];
                for (var z = 0; z < size; ++z)
                    target[z] = pkt->data[z + 1];

                outfile.Write(target, 0, size);
                ffmpeg.av_packet_unref(pkt);
            }
        } // ReSharper disable InconsistentNaming
        private const int EAGAIN = 11;

        /// <summary>
        ///     Try again
        /// </summary>
        private const int AVERROR_EAGAIN = -EAGAIN;

        /// <summary>
        ///     EOF or End-of-File
        /// </summary>
        private const int AVERROR_EOF = -0x20464F45;
    }
}