namespace Thea
{
    public class Message
    {
        public int MessageType { get; set; }
        public int CustomerId { get; set; }
        public string RequestType { get; set; }
        public string RequestKey { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public object Body { get; set; }
    }
}
