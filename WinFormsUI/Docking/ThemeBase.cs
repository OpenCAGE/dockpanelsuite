using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace WeifenLuo.WinFormsUI.Docking
{
    public abstract class ThemeBase : Component
    {
        private Color _dockBackColor;
        private bool _showAutoHideContentOnHover;

        protected ThemeBase()
        {
            Extender = new DockPanelExtender();
        }

        public DockPanelSkin Skin { get; protected set; }

        public DockPanelColorPalette ColorPalette { get; protected set; }

        public IImageService ImageService { get; protected set; }

        public IPaintingService PaintingService { get; protected set; }

        protected ToolStripRenderer ToolStripRenderer { get; set;}

        /* What each themed strip looked like before, for CleanUp to put back. Held weakly: every docked content's
           context and tab menus are themed as it is shown, and nothing took them out again, so each menu a closed
           content ever had - and, through its items' handlers, the content and everything it referenced - lived as
           long as the theme (OpenCAGE: 406 closed flowgraph pages and the previous level's scripts after one switch). */
        private sealed class StripBefore
        {
            public WeakReference Strip;
            public KeyValuePair<ToolStripRenderMode, ToolStripRenderer> Before;
        }

        private readonly List<StripBefore> _stripBefore = new List<StripBefore>();

        private void RememberStrip(ToolStrip toolStrip)
        {
            KeyValuePair<ToolStripRenderMode, ToolStripRenderer> before =
                new KeyValuePair<ToolStripRenderMode, ToolStripRenderer>(toolStrip.RenderMode, toolStrip.Renderer);
            for (int i = _stripBefore.Count - 1; i >= 0; i--)
            {
                ToolStrip strip = _stripBefore[i].Strip.Target as ToolStrip;
                if (strip == null || strip.IsDisposed)
                {
                    _stripBefore.RemoveAt(i);
                    continue;
                }
                if (strip == toolStrip)
                {
                    _stripBefore[i].Before = before;
                    return;
                }
            }
            _stripBefore.Add(new StripBefore { Strip = new WeakReference(toolStrip), Before = before });
        }

        public void ApplyTo(ToolStrip toolStrip)
        {
            if (toolStrip == null)
                return;

            RememberStrip(toolStrip);
            if(ToolStripRenderer != null)
                toolStrip.Renderer = ToolStripRenderer;

            if (Win32Helper.IsRunningOnMono)
            {
                foreach (var item in toolStrip.Items.OfType<ToolStripDropDownItem>())
                {
                    ItemResetOwnerHack(item);
                }
            }
        }

        private void ItemResetOwnerHack(ToolStripDropDownItem item)
        {
            var oldOwner = item.DropDown.OwnerItem;
            item.DropDown.OwnerItem = null;
            item.DropDown.OwnerItem = oldOwner;

            foreach (var child in item.DropDownItems.OfType<ToolStripDropDownItem>())
            {
                ItemResetOwnerHack(child);
            }
        }

        private KeyValuePair<ToolStripManagerRenderMode, ToolStripRenderer> _managerBefore;

        public void ApplyToToolStripManager()
        {
            _managerBefore = new KeyValuePair<ToolStripManagerRenderMode, ToolStripRenderer>(ToolStripManager.RenderMode, ToolStripManager.Renderer);
        }

        public Measures Measures { get; } = new Measures();

        public bool ShowAutoHideContentOnHover { get; protected set; } = true;

        public void ApplyTo(DockPanel dockPanel)
        {
            if (Extender.AutoHideStripFactory == null
                || Extender.AutoHideWindowFactory == null
                || Extender.DockIndicatorFactory == null
                || Extender.DockOutlineFactory == null
                || Extender.DockPaneCaptionFactory == null
                || Extender.DockPaneFactory == null
                || Extender.DockPaneSplitterControlFactory == null
                || Extender.DockPaneStripFactory == null
                || Extender.DockWindowFactory == null
                || Extender.FloatWindowFactory == null
                || Extender.PaneIndicatorFactory == null
                || Extender.PanelIndicatorFactory == null
                || Extender.WindowSplitterControlFactory == null)
            {
                throw new InvalidOperationException(Strings.Theme_MissingFactory);
            }

            if (dockPanel.Panes.Count > 0)
                throw new InvalidOperationException(Strings.Theme_PaneNotClosed);

            if (dockPanel.FloatWindows.Count > 0)
                throw new InvalidOperationException(Strings.Theme_FloatWindowNotClosed);

            if (dockPanel.Contents.Count > 0)
                throw new InvalidOperationException(Strings.Theme_DockContentNotClosed);

            if (ColorPalette == null)
            {
                dockPanel.ResetDummy();
            }
            else
            {
                _dockBackColor = dockPanel.DockBackColor;
                dockPanel.DockBackColor = ColorPalette.MainWindowActive.Background;
                dockPanel.SetDummy();
            }

            _showAutoHideContentOnHover = dockPanel.ShowAutoHideContentOnHover;
            dockPanel.ShowAutoHideContentOnHover = ShowAutoHideContentOnHover;
        }

        internal void PostApply(DockPanel dockPanel)
        {
            dockPanel.ResetAutoHideStripControl();
            dockPanel.ResetAutoHideStripWindow();
            dockPanel.ResetDockWindows();
        }

        public virtual void CleanUp(DockPanel dockPanel)
        {
            if (dockPanel != null)
            {
                if (ColorPalette != null)
                {
                    dockPanel.DockBackColor = _dockBackColor;
                }

                dockPanel.ShowAutoHideContentOnHover = _showAutoHideContentOnHover;
            }

            foreach (var item in _stripBefore)
            {
                var strip = item.Strip.Target as ToolStrip;
                if (strip == null || strip.IsDisposed)
                    continue;
                var cache = item.Before;
                if (cache.Key == ToolStripRenderMode.Custom)
                {
                    if (cache.Value != null)
                        strip.Renderer = cache.Value;
                }
                else
                {
                    strip.RenderMode = cache.Key;
                }
            }

            _stripBefore.Clear();
            if (_managerBefore.Key == ToolStripManagerRenderMode.Custom)
            {
                if (_managerBefore.Value != null)
                    ToolStripManager.Renderer = _managerBefore.Value;
            }
            else
            {
                ToolStripManager.RenderMode = _managerBefore.Key;
            }
        }

        public DockPanelExtender Extender { get; private set; }

        public static byte[] Decompress(byte[] fileToDecompress)
        {
            using (MemoryStream originalFileStream = new MemoryStream(fileToDecompress))
            {
                using (MemoryStream decompressedFileStream = new MemoryStream())
                {
                    using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                    {
                        //Copy the decompression stream into the output file.
                        byte[] buffer = new byte[4096];
                        int numRead;
                        while ((numRead = decompressionStream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            decompressedFileStream.Write(buffer, 0, numRead);
                        }

                        return decompressedFileStream.ToArray();
                    }
                }
            }
        }
    }
}
