using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Globalization;
using System.Windows.Forms;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using System.Runtime.InteropServices;

namespace ScalePlugin
{
    public class MenuScaleForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetFocus(IntPtr hWnd);

        // === Контролы ===
        private ComboBox cmbOptions;
        private TextBox textBox1;
        private Button btnAction1;
        private Button btnAction2;
        private Button commandButton1;

        // === Спойлер ===
        // private Button btnToggle;        // полоска «▼ Дополнительно»
        private Label btnToggle;         // полоска «▼ Дополнительно»
        private TableLayoutPanel chkPanel; // ← поле, а не локальная переменная
        private CheckBox chkDimensions;     // скрытый чекбокс размеров
        private CheckBox chkMLeaders;       // скрытый чекбокс выносок
        private CheckBox chkBlocks;         // скрытый чекбокс блоков
        private TableLayoutPanel root; // ссылка на корневой layout
        // Высоты клиентской области формы в двух состояниях
        private const int CollapsedClientHeight = 54;
        private const int ExpandedClientHeight  = 102;
        private const int HiddenRowHeight       = 0;
        private const int VisibleRowHeight      = 48;

        // === Поля формы ===
        private Document doc;
        private Database db;
        private Editor ed;

        // Захваченный набор объектов (Pickfirst на момент открытия формы)
        private ObjectId[] preselectedIds;

        public static string NewNameOfBblocksLayer = "1ЭП_Оформление";

        public MenuScaleForm()
        {
            doc = AcAp.DocumentManager.MdiActiveDocument;
            db = doc.Database;
            ed = doc.Editor;

            // Захватываем Pickfirst ДО того, как форма получит фокус.
            preselectedIds = GetImpliedSelectionIds();

            InitializeComponent();

            this.KeyPreview = true;
            this.KeyDown += Form_KeyDown;
        }

        /// <summary>
        /// Возвращает ObjectId[] из текущего Pickfirst либо null.
        /// </summary>
        private static ObjectId[] GetImpliedSelectionIds()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;

            PromptSelectionResult sel = doc.Editor.SelectImplied();
            if (sel.Status == PromptStatus.OK && sel.Value != null && sel.Value.Count > 0)
                return sel.Value.GetObjectIds();

            return null;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // ==================== ФОРМА ====================
            this.Text = "Применить масштаб";
            this.Font = new System.Drawing.Font("Tahoma", 7.8f);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new System.Drawing.Size(280, CollapsedClientHeight);

            // ==================== КОРНЕВОЙ LAYOUT (3 строки) ====================
            root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(4, 4, 4, 2),   // сверху 4, снизу 2
                Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));            // row0: кнопки + combo
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));            // row1: спойлер-полоска
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HiddenRowHeight)); // row2: скрытая секция

            // ==================== ROW0: кнопки + combo ====================
            var row1 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));

            btnAction2 = new Button
            {
                Text = "К модели",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 2, 0)
            };
            btnAction2.Click += BtnAction2_Click;

            cmbOptions = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(2, 0, 2, 0)
            };
            cmbOptions.Items.AddRange(new object[]
            {
                "Иное_значение",
                "100:1", "50:1", "40:1", "20:1", "10:1",
                "5:1", "4:1", "2.5:1", "2:1",
                "1:1",
                "1:2", "1:2.5", "1:4", "1:5",
                "1:10", "1:15", "1:20", "1:25",
                "1:40", "1:50", "1:75", "1:100",
                "1:200", "1:400", "1:500",
                "1:800", "1:1000"
            });
            cmbOptions.SelectedItem = "1:100";

            btnAction1 = new Button
            {
                Text = "К выбору",
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 0, 0)
            };
            btnAction1.Click += BtnAction1_Click;

            row1.Controls.Add(btnAction2, 0, 0);
            row1.Controls.Add(cmbOptions, 1, 0);
            row1.Controls.Add(btnAction1, 2, 0);

            // ==================== ROW1: спойлер-полоска ====================
            btnToggle = new Label
            {
                Text = "Параметры выбора ▼",
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                // FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                // TabStop = false,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Cursor = Cursors.Hand,
                BackColor = System.Drawing.Color.Transparent
            };
            // btnToggle.FlatAppearance.BorderSize = 0;
            btnToggle.Click += BtnToggle_Click;

            // ==================== ROW2: скрытая секция ====================
            //     Внутри — две под-строки:
            //       [0] три чекбокса
            //       [1] «Слой оформления» + textBox1
            chkPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Visible = false
            };
            chkPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            chkPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

            // ---- под-строка 0: чекбоксы ----
            var chkRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            chkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4f));
            chkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            chkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));

            chkDimensions = new CheckBox
            {
                Text = "размеры",
                Checked = true,
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };

            chkMLeaders = new CheckBox
            {
                Text = "выноски",
                Checked = true,
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };

            chkBlocks = new CheckBox
            {
                Text = "блоки",
                Checked = true,
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };

            chkRow.Controls.Add(chkDimensions, 0, 0);
            chkRow.Controls.Add(chkMLeaders, 1, 0);
            chkRow.Controls.Add(chkBlocks, 2, 0);

            // ---- под-строка 1: слой оформления + имя слоя ----
            var layerRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            layerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            layerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

            commandButton1 = new Button
            {
                Text = "Слой блоков оформления",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 2, 0)
            };
            commandButton1.Click += CommandButton1_Click;

            textBox1 = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(2, 0, 0, 0),
                Text = string.IsNullOrEmpty(NewNameOfBblocksLayer)
                    ? "1ЭП_Оформление"
                    : NewNameOfBblocksLayer
            };

            layerRow.Controls.Add(commandButton1, 0, 0);
            layerRow.Controls.Add(textBox1, 1, 0);

            // ---- сборка скрытой секции ----
            chkPanel.Controls.Add(chkRow,   0, 0);
            chkPanel.Controls.Add(layerRow, 0, 1);

            // ==================== СБОРКА КОРНЯ ====================
            root.Controls.Add(row1,     0, 0);
            root.Controls.Add(btnToggle, 0, 1);
            root.Controls.Add(chkPanel, 0, 2);

            this.Controls.Add(root);

            this.FormClosing += MenuScaleForm_FormClosing;

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // ==================== СПОЙЛЕР: РАСКРЫТЬ / СВЕРНУТЬ ====================

        private void BtnToggle_Click(object sender, EventArgs e)
        {
            bool expand = root.RowStyles[2].Height == HiddenRowHeight;

            root.RowStyles[2].Height = expand ? VisibleRowHeight : HiddenRowHeight;
            chkPanel.Visible = expand;

            btnToggle.Text = expand
                ? "Свернуть  ▲"
                : "Параметры выбора ▼";

            this.ClientSize = new System.Drawing.Size(
                this.ClientSize.Width,
                expand ? ExpandedClientHeight : CollapsedClientHeight);

            root.PerformLayout();  // на всякий случай — сразу переразложить
        }

        // ==================== ОБРАБОТЧИКИ ====================

        private void BtnAction1_Click(object sender, EventArgs e)
        {
            if (!TryGetScaleFromUI(out double factor))
                return;

            // Если пользователь ничего не выбрал до открытия формы —
            // попробуем захватить выбор ещё раз (на случай, если форма
            // открыта немодально и выбор сделали только что).
            if (preselectedIds == null || preselectedIds.Length == 0)
                preselectedIds = GetImpliedSelectionIds();

            this.Hide();
            try
            {
                SetScaleForSelection.Run(
                    factor,
                    textBox1.Text,
                    chkDimensions.Checked,
                    chkMLeaders.Checked,
                    chkBlocks.Checked,
                    preselectedIds);   // ← прокидываем захваченный набор
            }
            finally
            {
                // После применения масштаба набор уже неактуален.
                preselectedIds = null;

                this.Show();

                BeginInvoke(new Action(() =>
                {
                    GiveFocusToAutoCAD();
                }));
            }
        }

        private async void BtnAction2_Click(object sender, EventArgs e)
        {
            if (!TryGetScaleFromUI(out double factor))
                return;

            await SetScaleForDrawing.RunAsync(factor);

            BeginInvoke(new Action(() =>
            {
                GiveFocusToAutoCAD();
            }));
        }

        private void GiveFocusToAutoCAD()
        {
            Document activeDoc = AcAp.DocumentManager.MdiActiveDocument;
            if (activeDoc == null)
                return;

            IntPtr acadHandle = AcAp.MainWindow.Handle;
            if (acadHandle == IntPtr.Zero)
                return;

            SetForegroundWindow(acadHandle);

            IntPtr docHandle = activeDoc.Window.Handle;
            if (docHandle != IntPtr.Zero)
                SetFocus(docHandle);
        }

        private void CommandButton1_Click(object sender, EventArgs e)
        {
            this.Hide();
            try
            {
                string layer = SetScaleForSelection.PickBlockLayer();
                if (!string.IsNullOrEmpty(layer))
                {
                    NewNameOfBblocksLayer = layer;
                    textBox1.Text = layer;
                    MessageBox.Show(
                        $"Выбранный блок находится на слое: {layer}",
                        "Информация",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            finally { this.Show(); }
        }

        // ==================== ПАРСИНГ МАСШТАБА ====================

        private bool TryGetScaleFromUI(out double factor)
        {
            factor = 1.0;

            string value = cmbOptions.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value == "Иное_значение")
            {
                string input = PromptInput(
                    "Введите масштабный коэффициент:",
                    "Масштаб");

                if (string.IsNullOrWhiteSpace(input))
                    return false;

                if (!TryParseDouble(input, out factor))
                {
                    MessageBox.Show(
                        "Некорректный ввод. Введите положительное число.",
                        "Ошибка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return false;
                }

                return true;
            }

            string[] parts = value.Split(':');
            if (parts.Length != 2)
                return false;

            if (!TryParseDouble(parts[0], out double first))
                return false;

            if (!TryParseDouble(parts[1], out double second))
                return false;

            if (first <= 0 || second <= 0)
                return false;

            factor = second / first;
            return true;
        }

        private static bool TryParseDouble(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            if (double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value))
                return IsValidScale(value);

            if (double.TryParse(text, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out value))
                return IsValidScale(value);

            return false;
        }

        private static bool IsValidScale(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // ==================== ПРОЧЕЕ ====================

        private void MenuScaleForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!string.IsNullOrEmpty(textBox1.Text))
                NewNameOfBblocksLayer = textBox1.Text;
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
                e.Handled = true;
            }
        }

        private static string PromptInput(string prompt, string title)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = title;
                dlg.Font = new System.Drawing.Font("Tahoma", 8f);
                dlg.Width = 300;
                dlg.Height = 160;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;

                Label lbl = new Label { Text = prompt, Left = 10, Top = 10, Width = 270 };
                TextBox txt = new TextBox { Left = 10, Top = 40, Width = 265 };
                Button ok = new Button
                {
                    Text = "OK", Left = 110, Top = 80, Width = 75,
                    DialogResult = DialogResult.OK
                };
                Button cancel = new Button
                {
                    Text = "Отмена", Left = 195, Top = 80, Width = 75,
                    DialogResult = DialogResult.Cancel
                };

                dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                return dlg.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
            }
        }
    }
}