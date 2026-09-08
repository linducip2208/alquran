namespace QuranDesktop;

internal sealed class NavigationHistory
{
    private readonly List<(int Surah, int Ayah)> _items = new();
    private int _position = -1;

    public void Push(int surah, int ayah)
    {
        if (_position >= 0 && _position < _items.Count
            && _items[_position] == (surah, ayah)) return;

        while (_items.Count > _position + 1) _items.RemoveAt(_items.Count - 1);
        _items.Add((surah, ayah));
        if (_items.Count > 200) _items.RemoveAt(0);
        _position = _items.Count - 1;
    }

    public bool TryBack(out (int Surah, int Ayah) location)
    {
        if (_position <= 0)
        {
            location = default;
            return false;
        }

        location = _items[--_position];
        return true;
    }

    public bool TryForward(out (int Surah, int Ayah) location)
    {
        if (_position >= _items.Count - 1)
        {
            location = default;
            return false;
        }

        location = _items[++_position];
        return true;
    }
}
