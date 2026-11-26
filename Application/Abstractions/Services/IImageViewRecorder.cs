namespace Service.Contracts;

public interface IImageViewRecorder
{
    void Record(int imageId, int? userId);
}
