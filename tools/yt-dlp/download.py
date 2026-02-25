#!/usr/bin/env python3
"""
Video Downloader — yt-dlp wrapper for downloading videos.

Usage:
  python download.py <URL>
  python download.py <URL> --quality 720
  python download.py <URL> --audio-only
  python download.py <URL> --output /path/to/folder
  python download.py <URL> --list-formats
"""

import sys
import os
import shutil
import argparse
import yt_dlp


def progress_hook(d):
    if d['status'] == 'downloading':
        percent = d.get('_percent_str', '?%').strip()
        speed = d.get('_speed_str', '?').strip()
        eta = d.get('_eta_str', '?').strip()
        print(f"\r  {percent}  speed: {speed}  ETA: {eta}    ", end='', flush=True)
    elif d['status'] == 'finished':
        print(f"\r  Done! Processing file...                        ")
    elif d['status'] == 'error':
        print(f"\n  Download error")


def get_formats(url):
    """Show available formats for a URL."""
    opts = {'quiet': True, 'no_warnings': True}
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(url, download=False)
        formats = info.get('formats', [])
        print(f"\nAvailable formats for: {info.get('title', url)}\n")
        print(f"{'ID':<12} {'Ext':<6} {'Resolution':<12} {'FPS':<6} {'Size':<10} {'Codec'}")
        print("-" * 65)
        for f in formats:
            fid = f.get('format_id', '-')
            ext = f.get('ext', '-')
            res = f.get('resolution', f"{f.get('width','?')}x{f.get('height','?')}")
            fps = str(f.get('fps', '-'))
            size = f.get('filesize') or f.get('filesize_approx')
            size_str = f"{size/1024/1024:.1f}MB" if size else '-'
            vcodec = f.get('vcodec', '-')
            acodec = f.get('acodec', '-')
            codec = f"{vcodec}/{acodec}"[:20]
            print(f"{fid:<12} {ext:<6} {res:<12} {fps:<6} {size_str:<10} {codec}")


def find_ffmpeg():
    """Find ffmpeg binary."""
    for path in ['/opt/homebrew/bin/ffmpeg', '/usr/local/bin/ffmpeg', '/usr/bin/ffmpeg']:
        if os.path.isfile(path):
            return os.path.dirname(path)
    found = shutil.which('ffmpeg')
    if found:
        return os.path.dirname(found)
    return None


def download(url, output_dir, quality, audio_only, format_id=None):
    output_dir = output_dir or os.path.expanduser('~/Downloads')
    os.makedirs(output_dir, exist_ok=True)

    outtmpl = os.path.join(output_dir, '%(title)s.%(ext)s')

    if format_id:
        fmt = format_id
    elif audio_only:
        fmt = 'bestaudio/best'
    elif quality == 'best':
        fmt = 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best'
    elif quality == '1080':
        fmt = 'bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[height<=1080]'
    elif quality == '720':
        fmt = 'bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/best[height<=720]'
    elif quality == '480':
        fmt = 'bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/best[height<=480]'
    else:
        fmt = 'best[ext=mp4]/best'

    opts = {
        'format': fmt,
        'outtmpl': outtmpl,
        'progress_hooks': [progress_hook],
        'merge_output_format': 'mp4',
        'quiet': False,
        'no_warnings': False,
    }

    ffmpeg_dir = find_ffmpeg()
    if ffmpeg_dir:
        opts['ffmpeg_location'] = ffmpeg_dir

    if audio_only:
        opts['postprocessors'] = [{
            'key': 'FFmpegExtractAudio',
            'preferredcodec': 'mp3',
            'preferredquality': '192',
        }]

    print(f"\nDownloading: {url}")
    print(f"Output dir:  {output_dir}\n")

    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(url, download=False)
        title = info.get('title', 'video')
        duration = info.get('duration')
        uploader = info.get('uploader', '')
        print(f"Title:    {title}")
        if uploader:
            print(f"Author:   {uploader}")
        if duration:
            m, s = divmod(int(duration), 60)
            h, m = divmod(m, 60)
            print(f"Duration: {h:02d}:{m:02d}:{s:02d}")
        print()

        ydl.download([url])

    print(f"\nSaved to: {output_dir}\n")


def main():
    parser = argparse.ArgumentParser(
        description='Video Downloader — download videos via yt-dlp',
        formatter_class=argparse.RawTextHelpFormatter
    )
    parser.add_argument('url', nargs='?', help='Video URL')
    parser.add_argument('-o', '--output', help='Output directory (default: ~/Downloads)')
    parser.add_argument('-q', '--quality', default='best',
                        choices=['best', '1080', '720', '480'],
                        help='Video quality (default: best)')
    parser.add_argument('-a', '--audio-only', action='store_true',
                        help='Download audio only (MP3)')
    parser.add_argument('-f', '--format', dest='format_id',
                        help='Specific format ID (from --list-formats)')
    parser.add_argument('-l', '--list-formats', action='store_true',
                        help='List available formats without downloading')

    args = parser.parse_args()

    if not args.url:
        parser.print_help()
        sys.exit(1)

    if args.list_formats:
        get_formats(args.url)
        return

    try:
        download(args.url, args.output, args.quality, args.audio_only, args.format_id)
    except KeyboardInterrupt:
        print("\n\nCancelled")
    except Exception as e:
        print(f"\nError: {e}")
        sys.exit(1)


if __name__ == '__main__':
    main()