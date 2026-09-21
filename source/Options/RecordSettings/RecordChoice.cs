namespace Resonalyze.Options;

/// <summary>
/// A pick list held as its combo holds it: moving the selection raises <see cref="Changed"/>, clearing the list does not,
/// and selecting an absent item leaves the selection where it was.
/// </summary>
internal sealed class RecordChoice
{
    private readonly List<object> items = [];
    private int selectedIndex = -1;

    public event Action? Changed;

    public IReadOnlyList<object> Items => items;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, items.Count);
            if (value == selectedIndex)
            {
                return;
            }

            selectedIndex = value;
            Changed?.Invoke();
        }
    }

    public object? SelectedItem => selectedIndex >= 0 ? items[selectedIndex] : null;

    public void Clear()
    {
        items.Clear();
        selectedIndex = -1;
    }

    public void Add(object item) => items.Add(item);

    public void AddRange(IEnumerable<object> range) => items.AddRange(range);

    public int IndexOf(object item) => items.IndexOf(item);

    public void Select(object item)
    {
        int index = items.IndexOf(item);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
    }
}

/// <summary>A numeric field's value as the field shows it: assignments round and clamp, and only a move raises
/// <see cref="Changed"/>.</summary>
internal sealed class RecordNumber
{
    private decimal value;

    public RecordNumber(NumericFieldRange range, decimal value)
    {
        Range = range;
        this.value = range.Contain(value);
    }

    public event Action? Changed;

    public NumericFieldRange Range { get; }

    public decimal Value
    {
        get => value;
        set
        {
            decimal next = Range.Assign(value);
            if (next == this.value)
            {
                return;
            }

            this.value = next;
            Changed?.Invoke();
        }
    }
}
