// ============================================================================
//  GameObjectPanel.cs  —  Phase 5: WMO/M2 object list panel
//
//  Displays unique WMO and M2 filenames from the loaded ADT tile.
//  Provides visibility toggles for each category.
//  Events are wired to NavMeshRenderer in MainForm — not here.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeshViewer3D.Core.Formats.Adt;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel listing WMO and M2 objects loaded from an ADT tile.
    /// Mirrors the Honorbuddy Tripper.Renderer object list.
    /// </summary>
    public class GameObjectPanel : UserControl
    {
        // ── Controls ──────────────────────────────────────────────────────────
        private CheckBox _chkShowWmo = null!;
        private CheckBox _chkShowM2  = null!;
        private Label    _lblWmoCount = null!;
        private Label    _lblM2Count  = null!;
        private ListBox  _listWmo = null!;
        private ListBox  _listM2  = null!;
        private Label    _sepWmoM2 = null!;
        private Label    _lblM2Title = null!;
        private Label    _sepBake = null!;
        private Button   _btnBake = null!;
        private Button   _btnUnbake = null!;

        // Fixed heights for layout calculation
        private const int MARGIN       = 10;
        private const int LABEL_H      = 22;
        private const int CHECK_H      = 26;
        private const int SEP_H        = 10;
        private const int BTN_H        = 28;
        private const int BTN_GAP      = 6;
        private const int MIN_LIST_H   = 50;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fired when the "Show WMO" checkbox changes.</summary>
        public event Action<bool>? WmoVisibilityChanged;

        /// <summary>Fired when the "Show M2" checkbox changes.</summary>
        public event Action<bool>? M2VisibilityChanged;

        /// <summary>Fired when the user clicks "Bake Objects into Mesh".</summary>
        public event Action? BakeRequested;

        /// <summary>Fired when the user clicks "Unbake (Restore)".</summary>
        public event Action? UnbakeRequested;

        // ── Constructor ───────────────────────────────────────────────────────

        public GameObjectPanel()
        {
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Dock      = DockStyle.Fill;
            SetupUI();
            this.Resize += (_, _) => PerformLayout();
        }

        // ── UI setup ──────────────────────────────────────────────────────────

        private void SetupUI()
        {
            SuspendLayout();

            // ── WMO section ───────────────────────────────────────────────────
            var lblWmo = new Label
            {
                Text      = "WMO Objects",
                Location  = new Point(MARGIN, MARGIN),
                AutoSize  = true,
                ForeColor = Color.White,
                Font      = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblWmo);

            _lblWmoCount = new Label
            {
                Text      = "(0)",
                Location  = new Point(160, MARGIN),
                AutoSize  = true,
                ForeColor = Color.Gray
            };
            Controls.Add(_lblWmoCount);

            _chkShowWmo = new CheckBox
            {
                Text      = "Show WMO",
                Location  = new Point(MARGIN, 0), // repositioned in layout
                Size      = new Size(215, 20),
                ForeColor = Color.LightGray,
                Checked   = true
            };
            _chkShowWmo.CheckedChanged += (_, _) => WmoVisibilityChanged?.Invoke(_chkShowWmo.Checked);
            Controls.Add(_chkShowWmo);

            _listWmo = new ListBox
            {
                Location      = new Point(MARGIN, 0),
                Size          = new Size(215, 110),
                BackColor     = Color.FromArgb(30, 30, 30),
                ForeColor     = Color.White,
                BorderStyle   = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.One,
                IntegralHeight = false
            };
            Controls.Add(_listWmo);

            // ── Separator ─────────────────────────────────────────────────────
            _sepWmoM2 = new Label
            {
                Size      = new Size(215, 1),
                BackColor = Color.FromArgb(65, 65, 65)
            };
            Controls.Add(_sepWmoM2);

            // ── M2 section ────────────────────────────────────────────────────
            _lblM2Title = new Label
            {
                Text      = "M2 Objects",
                AutoSize  = true,
                ForeColor = Color.White,
                Font      = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(_lblM2Title);

            _lblM2Count = new Label
            {
                Text      = "(0)",
                AutoSize  = true,
                ForeColor = Color.Gray
            };
            Controls.Add(_lblM2Count);

            _chkShowM2 = new CheckBox
            {
                Text      = "Show M2",
                Size      = new Size(215, 20),
                ForeColor = Color.LightGray,
                Checked   = true
            };
            _chkShowM2.CheckedChanged += (_, _) => M2VisibilityChanged?.Invoke(_chkShowM2.Checked);
            Controls.Add(_chkShowM2);

            _listM2 = new ListBox
            {
                Size          = new Size(215, 110),
                BackColor     = Color.FromArgb(30, 30, 30),
                ForeColor     = Color.White,
                BorderStyle   = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.One,
                IntegralHeight = false
            };
            Controls.Add(_listM2);

            // ── Bake / Unbake buttons ─────────────────────────────────────────
            _sepBake = new Label
            {
                Size      = new Size(215, 1),
                BackColor = Color.FromArgb(65, 65, 65)
            };
            Controls.Add(_sepBake);

            _btnBake = new Button
            {
                Text      = "Bake Objects into Mesh",
                Size      = new Size(215, BTN_H),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.Gold,
                Cursor    = Cursors.Hand
            };
            _btnBake.Click += (_, _) => BakeRequested?.Invoke();
            Controls.Add(_btnBake);

            _btnUnbake = new Button
            {
                Text      = "Unbake (Restore)",
                Size      = new Size(215, BTN_H),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.Gray,
                Cursor    = Cursors.Hand
            };
            _btnUnbake.Click += (_, _) => UnbakeRequested?.Invoke();
            Controls.Add(_btnUnbake);

            ResumeLayout(false);
        }

        // ── Dynamic layout ────────────────────────────────────────────────────

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            DoLayout();
        }

        private void DoLayout()
        {
            int w = ClientSize.Width - MARGIN * 2;
            if (w < 60) w = 60;
            int h = ClientSize.Height;

            // Fixed vertical space consumed by labels, checkboxes, separators, buttons
            int fixedH = MARGIN          // top margin
                       + LABEL_H         // WMO label
                       + CHECK_H         // Show WMO checkbox
                       + 8               // gap after WMO list
                       + SEP_H           // separator
                       + LABEL_H         // M2 label
                       + CHECK_H         // Show M2 checkbox
                       + 8               // gap after M2 list
                       + SEP_H           // bake separator
                       + BTN_H + BTN_GAP // bake button
                       + BTN_H + MARGIN; // unbake button + bottom margin

            int availableForLists = h - fixedH;
            int listH = Math.Max(MIN_LIST_H, availableForLists / 2);

            int y = MARGIN;

            // WMO label row
            _lblWmoCount.Location = new Point(160, y);
            y += LABEL_H;

            // Show WMO checkbox
            _chkShowWmo.Location = new Point(MARGIN, y);
            _chkShowWmo.Width = w;
            y += CHECK_H;

            // WMO listbox
            _listWmo.SetBounds(MARGIN, y, w, listH);
            y += listH + 8;

            // Separator
            _sepWmoM2.SetBounds(MARGIN, y, w, 1);
            y += SEP_H;

            // M2 label row
            _lblM2Title.Location = new Point(MARGIN, y);
            _lblM2Count.Location = new Point(160, y);
            y += LABEL_H;

            // Show M2 checkbox
            _chkShowM2.Location = new Point(MARGIN, y);
            _chkShowM2.Width = w;
            y += CHECK_H;

            // M2 listbox
            _listM2.SetBounds(MARGIN, y, w, listH);
            y += listH + 8;

            // Bake separator
            _sepBake.SetBounds(MARGIN, y, w, 1);
            y += SEP_H;

            // Bake button
            _btnBake.SetBounds(MARGIN, y, w, BTN_H);
            y += BTN_H + BTN_GAP;

            // Unbake button
            _btnUnbake.SetBounds(MARGIN, y, w, BTN_H);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Populates the panel from a parsed ADT tile.
        /// Uses unique filenames (WmoNames/M2Names arrays) — not raw instance counts.
        /// Displays filename only (no directory path), e.g. "Stormwind.wmo".
        /// </summary>
        public void LoadObjects(AdtFile adt)
        {
            if (adt == null) { Clear(); return; }

            _listWmo.BeginUpdate();
            _listWmo.Items.Clear();
            var wmoNames = adt.WmoNames
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => Path.GetFileName(n))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n);
            foreach (var name in wmoNames) _listWmo.Items.Add(name);
            _listWmo.EndUpdate();
            _lblWmoCount.Text = $"({_listWmo.Items.Count})";

            _listM2.BeginUpdate();
            _listM2.Items.Clear();
            var m2Names = adt.M2Names
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => Path.GetFileName(n))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n);
            foreach (var name in m2Names) _listM2.Items.Add(name);
            _listM2.EndUpdate();
            _lblM2Count.Text = $"({_listM2.Items.Count})";
        }

        /// <summary>Clears all displayed objects and resets counts.</summary>
        public void Clear()
        {
            _listWmo.Items.Clear();
            _listM2.Items.Clear();
            _lblWmoCount.Text = "(0)";
            _lblM2Count.Text  = "(0)";
        }
    }
}
