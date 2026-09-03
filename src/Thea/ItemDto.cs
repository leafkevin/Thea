namespace Thea;

public class ItemDto<TValue>
{
    public string Text { get; set; }
    public TValue Value { get; set; }
}
public class ItemDto : ItemDto<string> { }