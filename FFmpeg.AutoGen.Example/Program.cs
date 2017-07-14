﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FFmpeg.AutoGen.Example
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            //EncodeSingleBitmap.video_encode_example(
            //   @"ThisIsATest.h264",
            //   (int)AVCodecID.AV_CODEC_ID_H264);

            var encoder = new EncodeMultipleBitmapsRgbaToYuv();
            encoder.video_encode_example(
                @"ThisIsATest.h264",
                (int)AVCodecID.AV_CODEC_ID_H264);

            Console.ReadKey();
        }


        //private static unsafe void Main(string[] args)
        //{
        //    Console.WriteLine("Decoding video...");
        //    var frames = DecodeVideo();
        //    Console.WriteLine("Decoded video successfully.");

        //    //Console.WriteLine("Encoding frames...");
        //    //var encoder = new EncodeMultipleBitmaps();
        //    //encoder.video_encode_example(
        //    //    @"ThisIsATest.h264",
        //    //    (int)AVCodecID.AV_CODEC_ID_H264);

        //    //Console.WriteLine("Encoded frames successfully...");

        //}

        private static unsafe List<Bitmap> DecodeVideo()
        {
            List<Bitmap> bitmaps = new List<Bitmap>();

            Console.WriteLine(@"Current directory: " + Environment.CurrentDirectory);
            Console.WriteLine(@"Runnung in {0}-bit mode.", Environment.Is64BitProcess ? @"64" : @"32");

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

            // setup logging
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);
            av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;

                var lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);
                Console.Write(line);
            };
            ffmpeg.av_log_set_callback(logCallback);

            // decode N frames from url or path

            //string url = @"../../sample_mpeg4.mp4";
            var url = @"http://www.quirksmode.org/html5/videos/big_buck_bunny.mp4";

            var pFormatContext = ffmpeg.avformat_alloc_context();

            if (ffmpeg.avformat_open_input(&pFormatContext, url, null, null) != 0)
                throw new ApplicationException(@"Could not open file");

            if (ffmpeg.avformat_find_stream_info(pFormatContext, null) != 0)
                throw new ApplicationException(@"Could not find stream info");

            AVStream* pStream = null;
            for (var i = 0; i < pFormatContext->nb_streams; i++)
                if (pFormatContext->streams[i]->codec->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    pStream = pFormatContext->streams[i];
                    break;
                }
            if (pStream == null)
                throw new ApplicationException(@"Could not found video stream");


            var codecContext = *pStream->codec;

            Console.WriteLine($"codec name: {ffmpeg.avcodec_get_name(codecContext.codec_id)}");

            var width = codecContext.width;
            var height = codecContext.height;
            var sourcePixFmt = codecContext.pix_fmt;
            var codecId = codecContext.codec_id;
            var destinationPixFmt = AVPixelFormat.AV_PIX_FMT_BGR24;
            var pConvertContext = ffmpeg.sws_getContext(width, height, sourcePixFmt,
                width, height, destinationPixFmt,
                ffmpeg.SWS_FAST_BILINEAR, null, null, null);
            if (pConvertContext == null)
                throw new ApplicationException(@"Could not initialize the conversion context");

            var pConvertedFrame = ffmpeg.av_frame_alloc();
            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixFmt, width, height, 1);
            var convertedFrameBuffer = stackalloc byte[convertedFrameBufferSize];
            var dstData = new byte_ptrArray4();
            var dstLinesize = new int_array4();
            ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, convertedFrameBuffer, destinationPixFmt, width, height, 1);

            var pCodec = ffmpeg.avcodec_find_decoder(codecId);
            if (pCodec == null)
                throw new ApplicationException(@"Unsupported codec");

            // reusing codec context from stream info, initally it was looking like this: 
            // AVCodecContext* pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec); // but this is not working for all kind of codecs
            var pCodecContext = &codecContext;

            if ((pCodec->capabilities & ffmpeg.AV_CODEC_CAP_TRUNCATED) == ffmpeg.AV_CODEC_CAP_TRUNCATED)
                pCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_TRUNCATED;

            if (ffmpeg.avcodec_open2(pCodecContext, pCodec, null) < 0)
                throw new ApplicationException(@"Could not open codec");

            var pDecodedFrame = ffmpeg.av_frame_alloc();

            var packet = new AVPacket();
            var pPacket = &packet;
            ffmpeg.av_init_packet(pPacket);

            var frameNumber = 0;
            //while (frameNumber < 200)
            while (true)
            {
                try
                {
                    if (ffmpeg.av_read_frame(pFormatContext, pPacket) < 0)
                        //throw new ApplicationException(@"Could not read frame");
                        break;

                    if (pPacket->stream_index != pStream->index)
                        continue;

                    if (ffmpeg.avcodec_send_packet(pCodecContext, pPacket) < 0)
                        throw new ApplicationException($@"Error while sending packet {frameNumber}");

                    if (ffmpeg.avcodec_receive_frame(pCodecContext, pDecodedFrame) < 0)
                        throw new ApplicationException($@"Error while receiving frame {frameNumber}");

                    Console.WriteLine($@"frame: {frameNumber}");

                    ffmpeg.sws_scale(pConvertContext, pDecodedFrame->data, pDecodedFrame->linesize, 0, height, dstData, dstLinesize);
                }
                finally
                {
                    ffmpeg.av_packet_unref(pPacket);
                    ffmpeg.av_frame_unref(pDecodedFrame);
                }

                var convertedFrameBufferPtr = (IntPtr)convertedFrameBuffer;

                using (var bitmap = new Bitmap(width, height, dstLinesize[0], PixelFormat.Format24bppRgb,
                    convertedFrameBufferPtr))
                {
                    bitmap.Save($@"frame.buffer{frameNumber}.jpg", ImageFormat.Jpeg);
                }

                frameNumber++;
            }

            ffmpeg.av_free(pConvertedFrame);
            ffmpeg.sws_freeContext(pConvertContext);

            ffmpeg.av_free(pDecodedFrame);
            ffmpeg.avcodec_close(pCodecContext);
            ffmpeg.avformat_close_input(&pFormatContext);

            return bitmaps;
        }
    }
}