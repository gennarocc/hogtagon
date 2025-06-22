import os
import sys
from moviepy import VideoFileClip

def crop_video_to_9_16(filepath):
    # Remove any quotes from the path
    filepath = filepath.strip().replace("'", "")
    
    # Load the video
    video = VideoFileClip(filepath)
    
    # Original dimensions
    width, height = video.size
    print(f"Original video dimensions: {width}x{height}")
    
    # Target aspect ratio 9:16
    target_aspect_ratio = 9 / 16
    
    # Calculate new width and height for the crop
    # We want to keep the height and crop width to match 9:16
    new_height = height
    new_width = int(new_height * target_aspect_ratio)
    
    # If the calculated width is larger than original, adjust by height instead
    if new_width > width:
        new_width = width
        new_height = int(new_width / target_aspect_ratio)
    
    # Calculate cropping coordinates (center crop)
    x1 = (width - new_width) // 2
    y1 = (height - new_height) // 2
    x2 = x1 + new_width
    y2 = y1 + new_height
    
    print(f"Cropping to: {new_width}x{new_height}")
    print(f"Crop coordinates: x1={x1}, y1={y1}, x2={x2}, y2={y2}")
    
    # Crop the video - using cropped method (correct method name)
    cropped_video = video.cropped(x1=x1, y1=y1, width=new_width, height=new_height)
    
    # Create output filename
    base, ext = os.path.splitext(filepath)
    output_filepath = f"{base}_9x16_crop{ext}"
    
    # Export the cropped video
    print("Exporting cropped video...")
    cropped_video.write_videofile(
        output_filepath, 
        codec='libx264', 
        audio_codec='aac',
        temp_audiofile='temp-audio.m4a',
        remove_temp=True
    )
    
    print(f"Cropped video saved to: {output_filepath}")
    
    # Clean up
    video.close()
    cropped_video.close()

if __name__ == '__main__':
    if len(sys.argv) > 1:
        filepath = sys.argv[1]
        crop_video_to_9_16(filepath)
    else:
        # Fallback to input if no command line arg is provided
        filepath = input("Enter the filepath of the video file (.mov, ignore single quotes): ")
        crop_video_to_9_16(filepath) 