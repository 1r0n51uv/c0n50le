using System.Drawing;
using System.Windows.Forms;

// A stand-in game: full screen, its name (the exe's) in the middle. Esc or the window's close button quits.
ApplicationConfiguration.Initialize();
var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "Test game";
var form = new Form
{
    Text = name,
    BackColor = Color.FromArgb(18, 22, 34),
    ForeColor = Color.White,
    WindowState = FormWindowState.Maximized,
    FormBorderStyle = FormBorderStyle.None,
    KeyPreview = true,
    StartPosition = FormStartPosition.CenterScreen
};
var title = new Label
{
    Text = name,
    Dock = DockStyle.Fill,
    TextAlign = ContentAlignment.MiddleCenter,
    Font = new Font("Segoe UI", 54, FontStyle.Bold)
};
var hint = new Label
{
    Text = "Test game for c0n50le · Esc to quit · Ctrl+Alt+F11 or View + Y back to the home",
    Dock = DockStyle.Bottom,
    Height = 80,
    TextAlign = ContentAlignment.MiddleCenter,
    Font = new Font("Segoe UI", 16),
    ForeColor = Color.FromArgb(170, 180, 195)
};
form.Controls.Add(title);
form.Controls.Add(hint);
form.KeyDown += (_, args) => { if (args.KeyCode == Keys.Escape) form.Close(); };
Application.Run(form);
