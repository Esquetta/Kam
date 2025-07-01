public interface IMusicService
{
    Task PlayMusicAsync(string filePath, bool loop = false);
    Task PauseMusicAsync();
    Task ResumeMusicAsync();
    Task StopMusicAsync();
    Task SetVolumeAsync(float volume);
}
