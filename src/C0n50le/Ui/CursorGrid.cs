using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace C0n50le.Ui;

public sealed class CursorGrid : Grid
{
    private readonly InputCursor _arrow = InputSystemCursor.Create(InputSystemCursorShape.Arrow);

    public void SetCursorHidden(bool hidden) => ProtectedCursor = hidden ? null : _arrow;
}
