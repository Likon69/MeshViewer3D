// ============================================================================
//  WmoBlacklistPanel.cs  —  WMO Blacklist panel
//
//  Allows the user to blacklist WMO buildings so they are hidden from the 3D
//  view. Checked = visible, unchecked = blacklisted (hidden).
//  Mirrors Honorbuddy NavMesh viewer's WMO ignore feature.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel with a CheckedListBox for toggling WMO visibility.
    /// Checked items are visible; unchecked items are blacklisted.
    /// </summary>
    public class WmoBlacklistPanel : UserControl
    {
        // ── Controls ──────────────────────────────────────────────────────────
        private CheckedListBox _checkedList = null!;
        private Label _lblCount = null!;
        private Button _btnSelectAll = null!;
        private Button _btnDeselectAll = null!;
        private Button _btnExport = null!;
        private Button _btnImport = null!;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly HashSet<string> _blacklisted = new(StringComparer.OrdinalIgnoreCase);

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fired when the blacklist changes. Contains the set of blacklisted WMO names.</summary>
        public event Action<HashSet<string>>? BlacklistChanged;

        // ── Constructor ───────────────────────────────────────────────────────

        public WmoBlacklistPanel()
        {
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Dock = DockStyle.Fill;
            SetupUI();
        }

        // ── UI setup ──────────────────────────────────────────────────────────

        private void SetupUI()
        {
            int y = 10;

            // Header
            var lblHeader = new Label
            {
                Text = "WMO Blacklist",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblHeader);

            _lblCount = new Label
            {
                Text = "(0)",
                Location = new Point(160, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            Controls.Add(_lblCount);
            y += 22;

            // Info label
            var lblInfo = new Label
            {
                Text = "Checked = visible, Unchecked = hidden",
                Location = new Point(10, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(140, 140, 140),
                Font = new Font(Font.FontFamily, 7.5f)
            };
            Controls.Add(lblInfo);
            y += 18;

            // CheckedListBox
            _checkedList = new CheckedListBox
            {
                Location = new Point(10, y),
                Size = new Size(215, 180),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                IntegralHeight = false
            };
            _checkedList.ItemCheck += OnItemCheck;
            Controls.Add(_checkedList);
            y += 188;

            // Select All / Deselect All
            _btnSelectAll = CreateButton("Select All", 10, y);
            _btnSelectAll.Size = new Size(105, 25);
            _btnSelectAll.Click += OnSelectAll;
            Controls.Add(_btnSelectAll);

            _btnDeselectAll = CreateButton("Deselect All", 120, y);
            _btnDeselectAll.Size = new Size(105, 25);
            _btnDeselectAll.Click += OnDeselectAll;
            Controls.Add(_btnDeselectAll);
            y += 32;

            // Export / Import
            _btnExport = CreateButton("Export Blacklist", 10, y);
            _btnExport.Size = new Size(105, 25);
            _btnExport.Click += OnExport;
            Controls.Add(_btnExport);

            _btnImport = CreateButton("Import Blacklist", 120, y);
            _btnImport.Size = new Size(105, 25);
            _btnImport.Click += OnImport;
            Controls.Add(_btnImport);
        }

        private Button CreateButton(string text, int x, int y)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(75, 25),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            return btn;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Populates the list from an array of WMO name strings (from AdtFile.WmoNames).
        /// Extracts filename only (no path). All items start checked (visible).
        /// </summary>
        public void LoadWmoNames(string[] wmoNames)
        {
            _checkedList.ItemCheck -= OnItemCheck;
            _checkedList.Items.Clear();
            _blacklisted.Clear();

            var unique = (wmoNames ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => Path.GetFileName(n))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            foreach (var name in unique)
                _checkedList.Items.Add(name, isChecked: true); // checked = visible

            _lblCount.Text = $"({unique.Count})";
            _checkedList.ItemCheck += OnItemCheck;
            Console.WriteLine($"[WmoBlacklistPanel] Loaded {unique.Count} WMO names");
        }

        /// <summary>Clears everything.</summary>
        public void Clear()
        {
            _checkedList.Items.Clear();
            _blacklisted.Clear();
            _lblCount.Text = "(0)";
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnItemCheck(object? sender, ItemCheckEventArgs e)
        {
            string name = _checkedList.Items[e.Index]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return;

            if (e.NewValue == CheckState.Unchecked)
                _blacklisted.Add(name);
            else
                _blacklisted.Remove(name);

            // Fire after the check state is applied (use BeginInvoke to let WinForms finish)
            BeginInvoke(() =>
            {
                Console.WriteLine($"[WmoBlacklistPanel] Blacklist updated: {_blacklisted.Count} hidden");
                BlacklistChanged?.Invoke(new HashSet<string>(_blacklisted, StringComparer.OrdinalIgnoreCase));
            });
        }

        private void OnSelectAll(object? sender, EventArgs e)
        {
            _checkedList.ItemCheck -= OnItemCheck;
            for (int i = 0; i < _checkedList.Items.Count; i++)
                _checkedList.SetItemChecked(i, true);
            _blacklisted.Clear();
            _checkedList.ItemCheck += OnItemCheck;
            Console.WriteLine("[WmoBlacklistPanel] Select All — 0 blacklisted");
            BlacklistChanged?.Invoke(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private void OnDeselectAll(object? sender, EventArgs e)
        {
            _checkedList.ItemCheck -= OnItemCheck;
            _blacklisted.Clear();
            for (int i = 0; i < _checkedList.Items.Count; i++)
            {
                _checkedList.SetItemChecked(i, false);
                string name = _checkedList.Items[i]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(name)) _blacklisted.Add(name);
            }
            _checkedList.ItemCheck += OnItemCheck;
            Console.WriteLine($"[WmoBlacklistPanel] Deselect All — {_blacklisted.Count} blacklisted");
            BlacklistChanged?.Invoke(new HashSet<string>(_blacklisted, StringComparer.OrdinalIgnoreCase));
        }

        private void OnExport(object? sender, EventArgs e)
        {
            if (_blacklisted.Count == 0)
            {
                MessageBox.Show("No WMOs are blacklisted.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "Text files|*.txt|All files|*.*",
                Title = "Export WMO Blacklist",
                FileName = "wmo_blacklist.txt"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllLines(dlg.FileName, _blacklisted.OrderBy(n => n));
                Console.WriteLine($"[WmoBlacklistPanel] Exported {_blacklisted.Count} entries to {dlg.FileName}");
            }
        }

        private void OnImport(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Text files|*.txt|All files|*.*",
                Title = "Import WMO Blacklist"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var lines = File.ReadAllLines(dlg.FileName)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _checkedList.ItemCheck -= OnItemCheck;
            _blacklisted.Clear();
            for (int i = 0; i < _checkedList.Items.Count; i++)
            {
                string name = _checkedList.Items[i]?.ToString() ?? "";
                bool isBlacklisted = lines.Contains(name);
                _checkedList.SetItemChecked(i, !isBlacklisted);
                if (isBlacklisted) _blacklisted.Add(name);
            }
            _checkedList.ItemCheck += OnItemCheck;

            Console.WriteLine($"[WmoBlacklistPanel] Imported blacklist: {_blacklisted.Count} hidden");
            BlacklistChanged?.Invoke(new HashSet<string>(_blacklisted, StringComparer.OrdinalIgnoreCase));
        }
    }
}
