namespace OVK_Music
{
    public class PlaylistItem
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int Size { get; set; }
        public int Length { get; set; }
        public int Created { get; set; }
        public int? Modified { get; set; }
        public bool Accessible { get; set; }
        public bool Editable { get; set; }
        public bool Bookmarked { get; set; }
        public int Listens { get; set; }
        public string CoverUrl { get; set; }
        public bool Searchable { get; set; }
    }
}
