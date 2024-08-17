namespace Shared.DataTransferObjects;

    public class AppImageDTO
    {
        public int Id { get; set; }
        public string UploadedBy { get; set; }
        public DateTime UploadDate { get; set; }
        public string UrlToImage { get; set; }
    }