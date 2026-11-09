
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChillDisplays
{
    public partial class Form1 : Form
    {
        public bool toggled = true;
        public bool insights_mode = false;
        public bool editor_mode = false;
        public bool wasComboPressedLastTick = false;

        public bool isMonitorEnabledByUI { get; set; } = true;
        public bool IsPrimaryForm { get; set; } = true;
        public Form1 MainController { get; set; }
        public string MonitorDeviceName { get; set; }

        private readonly Dictionary<string, Form1> childForms =
            new Dictionary<string, Form1>();

        private bool populatingMonitorList;
        private bool shortcutWasHeld;

        private static Form1 activeEditor;

        private Color sharedBackgroundColor = Color.Black;
        private string sharedBackgroundImagePath;

        private FlowLayoutPanel editorPanel;
        private Button colorButton;
        private Button imageButton;
        private Panel dropPanel;
        private Label dropLabel;

        private readonly Color editorBackgroundColor =
            Color.FromArgb(34, 36, 42);

        private const int VK_RBUTTON = 0x02;
        private const int VK_NUMPAD_MULTIPLY = 0x6A;
        private const int VK_SUBTRACT = 0x6D;
        private const int VK_OEM_MINUS = 0xBD;
        private const int KEY_DOWN_MASK = 0x8000;

        // Windows API constants used to maintain window z-order.
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", EntryPoint = "IsIconic")]
        private static extern bool IsWindowMinimized(IntPtr hWnd);

        public Form1()
        {
            InitializeComponent();
            CreateEditorControls();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;

            // Keep cover windows above normal application windows.
            TopMost = true;

            // Only the primary form polls keyboard state.
            timer1.Interval = 50;
            timer1.Enabled = IsPrimaryForm;

            MonitorsList.ItemCheck -= MonitorsList_ItemCheck;
            MonitorsList.ItemCheck += MonitorsList_ItemCheck;

            if (IsPrimaryForm)
                MonitorDeviceName = Screen.PrimaryScreen.DeviceName;

            if (string.IsNullOrEmpty(MonitorDeviceName))
                MonitorDeviceName = Screen.FromControl(this).DeviceName;

            SetMonitorBounds();
            PopulateMonitorList();
            ApplyEditorMode();

            if (Visible)
                EnsureCoverOnTop();
        }

        private Form1 GetController()
        {
            return IsPrimaryForm ? this : MainController;
        }

        private Screen GetAssignedScreen()
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.DeviceName == MonitorDeviceName)
                    return screen;
            }

            return Screen.PrimaryScreen;
        }

        private void SetMonitorBounds()
        {
            if (IsDisposed || Disposing)
                return;

            Screen screen = GetAssignedScreen();

            if (screen != null)
                Bounds = screen.Bounds;
        }

        // Reassert topmost z-order without moving, resizing, or activating
        // the window. Call this when showing or restoring a cover window.
        private void EnsureCoverOnTop()
        {
            if (IsDisposed ||
                Disposing ||
                !IsHandleCreated ||
                !Visible)
            {
                return;
            }

            TopMost = true;

            SetWindowPos(
                Handle,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE |
                SWP_NOSIZE |
                SWP_NOACTIVATE |
                SWP_SHOWWINDOW);
        }

        private void CreateEditorControls()
        {
            editorPanel = new FlowLayoutPanel
            {
                Name = "editorPanel",
                AutoSize = false,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Width = 230,
                Height = 220,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(235, 35, 35, 35),
                Visible = false,
                Location = new Point(12, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            colorButton = new Button
            {
                Text = "Choose background color",
                Width = 205,
                Height = 34
            };

            colorButton.Click += ChooseBackgroundColor;

            imageButton = new Button
            {
                Text = "Choose background image",
                Width = 205,
                Height = 34
            };

            imageButton.Click += ChooseBackgroundImage;

            dropPanel = new Panel
            {
                Width = 205,
                Height = 95,
                AllowDrop = true,
                BackColor = Color.FromArgb(65, 65, 65),
                BorderStyle = BorderStyle.FixedSingle
            };

            dropLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Drop an image here",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                AllowDrop = true
            };

            dropPanel.Controls.Add(dropLabel);

            dropPanel.DragEnter += ImageDragEnter;
            dropPanel.DragDrop += ImageDragDrop;

            dropLabel.DragEnter += ImageDragEnter;
            dropLabel.DragDrop += ImageDragDrop;

            editorPanel.Controls.Add(colorButton);
            editorPanel.Controls.Add(imageButton);
            editorPanel.Controls.Add(dropPanel);

            Controls.Add(editorPanel);
            editorPanel.BringToFront();
        }

        private void ChooseBackgroundColor(object sender, EventArgs e)
        {
            Form1 controller = GetController();

            if (controller == null || controller.IsDisposed)
                return;

            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = controller.sharedBackgroundColor;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    controller.sharedBackgroundColor = dialog.Color;
                    controller.sharedBackgroundImagePath = null;
                    controller.ApplyBackgroundToAllWindows();
                }
            }
        }

        private void ChooseBackgroundImage(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose background image";
                dialog.Filter =
                    "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif";

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetSharedBackgroundImage(dialog.FileName);
            }
        }

        private void ImageDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files =
                    (string[])e.Data.GetData(DataFormats.FileDrop);

                e.Effect =
                    files.Length > 0 && IsImageFile(files[0])
                        ? DragDropEffects.Copy
                        : DragDropEffects.None;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void ImageDragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] files =
                (string[])e.Data.GetData(DataFormats.FileDrop);

            if (files.Length > 0 && IsImageFile(files[0]))
                SetSharedBackgroundImage(files[0]);
        }

        private bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string extension = Path.GetExtension(path).ToLowerInvariant();

            return extension == ".png" ||
                   extension == ".jpg" ||
                   extension == ".jpeg" ||
                   extension == ".bmp" ||
                   extension == ".gif";
        }

        private void SetSharedBackgroundImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                using (Image test = Image.FromFile(path))
                {
                    if (test.Width <= 0 || test.Height <= 0)
                        return;
                }

                Form1 controller = GetController();

                if (controller == null || controller.IsDisposed)
                    return;

                controller.sharedBackgroundImagePath = path;
                controller.ApplyBackgroundToAllWindows();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not load this image: " + ex.Message,
                    "ChillDisplays",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ApplyBackgroundToAllWindows()
        {
            Form1 controller = GetController();

            if (controller == null || controller.IsDisposed)
                return;

            controller.ApplyBackground();

            foreach (Form1 child in controller.childForms.Values)
            {
                if (child != null && !child.IsDisposed)
                    child.ApplyBackground();
            }
        }

        private void ApplyBackground()
        {
            Form1 controller = GetController();

            if (controller == null || controller.IsDisposed)
                return;

            if (editor_mode)
            {
                SetEditorBackground();
                return;
            }

            BackgroundImageLayout = ImageLayout.Stretch;

            Image oldImage = BackgroundImage;
            BackgroundImage = null;

            if (oldImage != null)
                oldImage.Dispose();

            if (!string.IsNullOrEmpty(controller.sharedBackgroundImagePath) &&
                File.Exists(controller.sharedBackgroundImagePath))
            {
                try
                {
                    using (Image source =
                           Image.FromFile(controller.sharedBackgroundImagePath))
                    {
                        BackgroundImage = new Bitmap(source);
                    }
                }
                catch
                {
                    BackColor = controller.sharedBackgroundColor;
                }
            }
            else
            {
                BackColor = controller.sharedBackgroundColor;
            }

            Invalidate();
        }

        private void SetEditorBackground()
        {
            Image oldImage = BackgroundImage;
            BackgroundImage = null;

            if (oldImage != null)
                oldImage.Dispose();

            BackColor = editorBackgroundColor;
            Invalidate();
        }

        private void PopulateMonitorList()
        {
            Form1 controller = GetController();

            if (controller == null ||
                controller.IsDisposed ||
                IsDisposed ||
                MonitorsList == null)
            {
                return;
            }

            populatingMonitorList = true;

            try
            {
                Screen[] screens = Screen.AllScreens;
                MonitorsList.Items.Clear();

                for (int i = 0; i < screens.Length; i++)
                {
                    MonitorsList.Items.Add(screens[i].DeviceName);

                    bool enabled = false;

                    if (screens[i].Primary)
                    {
                        enabled = controller.isMonitorEnabledByUI;
                    }
                    else if (controller.childForms.TryGetValue(
                        screens[i].DeviceName, out Form1 child))
                    {
                        enabled =
                            child != null &&
                            !child.IsDisposed &&
                            child.isMonitorEnabledByUI;
                    }

                    MonitorsList.SetItemChecked(i, enabled);
                }
            }
            finally
            {
                populatingMonitorList = false;
            }
        }

        private void RefreshMonitorLists()
        {
            Form1 controller = GetController();

            if (controller == null || controller.IsDisposed)
                return;

            controller.PopulateMonitorList();

            foreach (Form1 child in controller.childForms.Values)
            {
                if (child != null && !child.IsDisposed)
                    child.PopulateMonitorList();
            }
        }

        private bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & KEY_DOWN_MASK) != 0;
        }

        private bool IsMinusKeyPressed()
        {
            // Supports both numeric keypad minus and regular minus.
            return IsKeyDown(VK_SUBTRACT) ||
                   IsKeyDown(VK_OEM_MINUS);
        }

        private void HandleEditorShortcut()
        {
            bool held =
                IsKeyDown(VK_NUMPAD_MULTIPLY) &&
                IsKeyDown(VK_RBUTTON);

            if (held && !shortcutWasHeld)
            {
                Point cursor = Cursor.Position;
                Screen screen = Screen.FromPoint(cursor);
                Form1 target = null;

                if (screen.Primary)
                {
                    target = this;
                }
                else
                {
                    childForms.TryGetValue(screen.DeviceName, out target);
                }

                // Only activate the editor for an existing, visible,
                // enabled window containing the cursor.
                if (target != null &&
                    !target.IsDisposed &&
                    !target.Disposing &&
                    target.isMonitorEnabledByUI &&
                    target.Visible &&
                    target.Bounds.Contains(cursor) &&
                    !target.IsIconic())
                {
                    if (activeEditor != null &&
                        activeEditor != target &&
                        !activeEditor.IsDisposed)
                    {
                        activeEditor.editor_mode = false;
                        activeEditor.insights_mode = false;
                        activeEditor.ApplyEditorMode();
                    }

                    if (target.editor_mode)
                    {
                        target.editor_mode = false;
                        target.insights_mode = false;

                        if (activeEditor == target)
                            activeEditor = null;
                    }
                    else
                    {
                        target.editor_mode = true;
                        target.insights_mode = true;
                        activeEditor = target;
                    }

                    target.ApplyEditorMode();
                }
            }

            shortcutWasHeld = held;
            wasComboPressedLastTick = held;
        }

        private bool IsIconic()
        {
            return IsHandleCreated && IsWindowMinimized(Handle);
        }

        private void ApplyEditorMode()
        {
            if (IsDisposed || Disposing)
                return;

            // Cover windows stay borderless and non-resizable.
            FormBorderStyle = FormBorderStyle.None;

            if (label1 != null)
                label1.Visible = editor_mode;

            if (label2 != null)
                label2.Visible = editor_mode;

            if (label3 != null)
                label3.Visible = editor_mode;

            if (MonitorsList != null)
                MonitorsList.Visible = editor_mode;

            if (editorPanel != null)
                editorPanel.Visible = editor_mode;

            if (editor_mode)
            {
                SetEditorBackground();

                if (isMonitorEnabledByUI && !Visible)
                    Show();

                if (Visible)
                    EnsureCoverOnTop();

                if (editorPanel != null)
                    editorPanel.BringToFront();
            }
            else
            {
                SetMonitorBounds();
                ApplyBackground();

                if (Visible)
                    EnsureCoverOnTop();
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            // Child forms must never run their own keyboard/visibility loop.
            if (!IsPrimaryForm || IsDisposed || Disposing)
                return;

            bool minusPressed = IsMinusKeyPressed();

            HandleEditorShortcut();

            toggled = Control.IsKeyLocked(Keys.NumLock);

            if (label1 != null)
                label1.Text = MonitorDeviceName;

            if (label2 != null)
            {
                label2.Text = minusPressed
                    ? "minus held down!"
                    : "minus NOT held down!";
            }

            if (label3 != null)
            {
                label3.Text = toggled
                    ? "NumLock active"
                    : "NumLock inactive";
            }

            // The primary form owns the visibility state.
            UpdateWindowVisibility(minusPressed);

            // Use a snapshot in case the collection changes during iteration.
            List<Form1> children =
                new List<Form1>(childForms.Values);

            foreach (Form1 child in children)
            {
                if (child == null ||
                    child.IsDisposed ||
                    child.Disposing)
                {
                    continue;
                }

                if (child.label1 != null)
                    child.label1.Text = child.MonitorDeviceName;

                if (child.label2 != null)
                {
                    child.label2.Text = minusPressed
                        ? "minus held down!"
                        : "minus NOT held down!";
                }

                if (child.label3 != null)
                {
                    child.label3.Text = toggled
                        ? "NumLock active"
                        : "NumLock inactive";
                }

                child.UpdateWindowVisibility(minusPressed);
            }
        }

        private void UpdateWindowVisibility(bool minusPressed)
        {
            if (IsDisposed || Disposing)
                return;

            if (!isMonitorEnabledByUI)
            {
                if (Visible)
                    Hide();

                return;
            }

            // Editor mode must remain visible regardless of NumLock/minus.
            if (editor_mode)
            {
                if (!Visible)
                    Show();

                EnsureCoverOnTop();
                return;
            }

            SetMonitorBounds();

            bool shouldHide =
                minusPressed || toggledForThisWindow();

            if (shouldHide)
            {
                if (Visible)
                    Hide();
            }
            else
            {
                if (!Visible)
                {
                    Show();
                    EnsureCoverOnTop();
                }
                else if (!TopMost)
                {
                    // Recover if another part of the application changed it.
                    EnsureCoverOnTop();
                }
            }
        }

        private bool toggledForThisWindow()
        {
            Form1 controller = GetController();

            return controller != null && controller.toggled;
        }

        private void MonitorsList_ItemCheck(
            object sender,
            ItemCheckEventArgs e)
        {
            if (populatingMonitorList)
                return;

            Form1 controller = GetController();

            if (controller == null || controller.IsDisposed)
                return;

            Screen[] screens = Screen.AllScreens;

            if (e.Index < 0 || e.Index >= screens.Length)
                return;

            int checkedCount = 0;

            for (int i = 0; i < MonitorsList.Items.Count; i++)
            {
                bool isChecked = i == e.Index
                    ? e.NewValue == CheckState.Checked
                    : MonitorsList.GetItemChecked(i);

                if (isChecked)
                    checkedCount++;
            }

            if (checkedCount == 0)
            {
                e.NewValue = e.CurrentValue;

                MessageBox.Show(
                    "At least one monitor instance must remain active.",
                    "ChillDisplays Constraint",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            controller.HandleMonitorStateChange(
                screens[e.Index],
                e.NewValue == CheckState.Checked);

            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(new Action(controller.RefreshMonitorLists));
            }
        }

        public void HandleMonitorStateChange(
            Screen targetScreen,
            bool enabled)
        {
            if (targetScreen == null || IsDisposed || Disposing)
                return;

            string deviceName = targetScreen.DeviceName;

            if (targetScreen.Primary)
            {
                isMonitorEnabledByUI = enabled;

                if (!enabled)
                {
                    if (activeEditor == this)
                        activeEditor = null;

                    editor_mode = false;
                    insights_mode = false;

                    ApplyEditorMode();
                    Hide();
                }
                else
                {
                    // The primary timer will reconcile the visibility state.
                    if (!toggled && !IsMinusKeyPressed())
                    {
                        Show();
                        EnsureCoverOnTop();
                    }
                }

                return;
            }

            if (enabled)
            {
                if (childForms.TryGetValue(
                    deviceName, out Form1 existing) &&
                    existing != null &&
                    !existing.IsDisposed)
                {
                    existing.isMonitorEnabledByUI = true;
                    existing.SetMonitorBounds();

                    if (existing.editor_mode ||
                        (!toggled && !IsMinusKeyPressed()))
                    {
                        existing.Show();
                        existing.EnsureCoverOnTop();
                    }
                }
                else
                {
                    childForms.Remove(deviceName);
                    CreateAndShowChildForm(targetScreen);
                }
            }
            else
            {
                if (childForms.TryGetValue(
                    deviceName, out Form1 child))
                {
                    childForms.Remove(deviceName);

                    if (child != null && !child.IsDisposed)
                    {
                        if (activeEditor == child)
                            activeEditor = null;

                        child.editor_mode = false;
                        child.insights_mode = false;
                        child.isMonitorEnabledByUI = false;

                        child.Hide();
                        child.Close();
                        child.Dispose();
                    }
                }
            }

            RefreshMonitorLists();
        }

        private void CreateAndShowChildForm(Screen targetScreen)
        {
            if (targetScreen == null)
                return;

            if (childForms.TryGetValue(
                targetScreen.DeviceName, out Form1 existing) &&
                existing != null &&
                !existing.IsDisposed)
            {
                return;
            }

            Form1 child = new Form1
            {
                IsPrimaryForm = false,
                MainController = this,
                MonitorDeviceName = targetScreen.DeviceName,
                isMonitorEnabledByUI = true,
                StartPosition = FormStartPosition.Manual,
                Location = targetScreen.Bounds.Location,
                Size = targetScreen.Bounds.Size,
                FormBorderStyle = FormBorderStyle.None,
                TopMost = true
            };

            child.sharedBackgroundColor = sharedBackgroundColor;
            child.sharedBackgroundImagePath = sharedBackgroundImagePath;

            childForms[targetScreen.DeviceName] = child;

            // Form_Load disables the child's timer.
            child.Show();
            child.EnsureCoverOnTop();
        }

        private void MonitorsList_SelectedValueChanged(
            object sender,
            EventArgs e)
        {
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (activeEditor == this)
                activeEditor = null;

            if (BackgroundImage != null)
            {
                Image oldImage = BackgroundImage;
                BackgroundImage = null;
                oldImage.Dispose();
            }

            base.OnFormClosed(e);
        }
    }
}
