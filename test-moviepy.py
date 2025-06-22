try:
    from moviepy import VideoFileClip
    print("Successfully imported VideoFileClip")
except Exception as e:
    print(f"Error: {e}")

from moviepy import VideoFileClip

# Create a test video clip
clip = VideoFileClip("C:\\Users\\Char\\hogtagon\\Recordings\\Movie_001.mp4")

# Get all methods and attributes
methods = [method for method in dir(clip) if not method.startswith('_')]
print("Available methods and attributes:")
for method in methods:
    print(method)

# Close the clip
clip.close() 