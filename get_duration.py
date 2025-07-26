from pymediainfo import MediaInfo
import sys

def get_duration(filepath):
    media_info = MediaInfo.parse(filepath)
    for track in media_info.tracks:
        if track.track_type == 'General':
            duration_ms = track.duration
            if duration_ms:
                seconds = int(duration_ms / 1000)
                minutes = seconds // 60
                seconds = seconds % 60
                return minutes, seconds
    return None, None

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("0,0")
        sys.exit(1)
    filepath = sys.argv[1]
    mins, secs = get_duration(filepath)
    if mins is not None:
        print(f"{mins},{secs}")
    else:
        print("0,0")
