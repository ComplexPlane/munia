﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MUNIA.Controllers;
using MUNIA.Interop;
using MUNIA.Properties;
using MUNIA.Skinning;
using MUNIA.Util;
using OpenTK.Graphics.OpenGL;

namespace MUNIA.Forms {
	public partial class MainForm : Form {
		private double _timestamp;
		private int _frames;
		private readonly bool _skipUpdateCheck;
		private int _fps;

		private MainForm() {
			InitializeComponent();
		}

		public MainForm(bool skipUpdateCheck) : this() {
			glControl.Resize += OnResize;
			glControl.Paint += (sender, args) => Render();
			UsbNotification.DeviceArrival += OnDeviceArrival;
			UsbNotification.DeviceRemovalComplete += OnDeviceRemoval;
			//UsbNotification.SetFilter(G)
			_skipUpdateCheck = skipUpdateCheck;
		}

		private void MainForm_Shown(object sender, EventArgs e) {
			ConfigManager.Load();

			tsmiBackgroundTransparent.Checked = ConfigManager.BackgroundColor.A == 0;
			BuildMenu();
			ActivateConfig(ConfigManager.GetActiveController(), ConfigManager.ActiveSkin);

			Application.Idle += OnApplicationOnIdle;
			if (!_skipUpdateCheck)
				PerformUpdateCheck();
			else
				UpdateStatus("not checking for newer version", 100);
		}

		private Task _buildMenuTask;
		private async void ScheduleBuildMenu() {
			// buffers multiple BuildMenu calls
			if (_buildMenuTask == null) {
				_buildMenuTask = Task.Delay(300).ContinueWith(t => Invoke((Action)delegate {
					ConfigManager.LoadControllers();
					BuildMenu();
				}));
				await _buildMenuTask;
				_buildMenuTask = null;
			}
		}

		private void BuildMenu() {
			_buildMenuTask = null;
			Debug.WriteLine("Building menu");

			tsmiControllers.DropDownItems.Clear();

			// first show the controllers which are available
			foreach (var ctrlr in ConfigManager.Controllers.OrderBy(c => c.Name)) {
				var tsmiController = new ToolStripMenuItem(ctrlr.Name);

				foreach (var skin in ConfigManager.Skins.Where(s => s.Controllers.Contains(ctrlr.Type))) {
					var tsmiSkin = new ToolStripMenuItem($"{skin.Name}");
					tsmiSkin.Enabled = true;
					tsmiSkin.ToolTipText = skin.Path;
					tsmiSkin.Click += (sender, args) => ActivateConfig(ctrlr, skin);
					tsmiSkin.Image = GetSkinImage(skin);
					tsmiController.DropDownItems.Add(tsmiSkin);
				}

				tsmiController.Image = GetControllerImage(ctrlr);
				tsmiControllers.DropDownItems.Add(tsmiController);
			}

			// then grab the controllers from skins that weren't found
			var allControllerTypes = ConfigManager.Skins.SelectMany(s => s.Controllers).ToList();
			var availableControllers = ConfigManager.Controllers.Select(c => c.Type).ToList();

			if (availableControllers.Count() < allControllerTypes.Count())
				tsmiControllers.DropDownItems.Add(new ToolStripSeparator());

			// make 'empty'  menu entries for controllers that aren't detected,
			// so that we can still look at the skins for them
			foreach (var controller in allControllerTypes.Except(availableControllers).OrderBy(c => c.ToString())) {
				var tsmiSkin = new ToolStripMenuItem(controller.ToString());

				var preview = tsmiSkin.DropDownItems.Add("No controllers - preview only");
				preview.Enabled = false;

				foreach (var skin in ConfigManager.Skins.Where(s => s.Controllers.Contains(controller))) {
					var skinPrev = tsmiSkin.DropDownItems.Add(skin.Name);
					skinPrev.Enabled = true;
					skinPrev.Click += (sender, args) => ActivateConfig(null, skin);
					skinPrev.Image = GetSkinImage(skin);
					tsmiSkin.Image = GetControllerImage(controller);
				}

				tsmiControllers.DropDownItems.Add(tsmiSkin);
			}

			string skinText = $"Loaded {ConfigManager.Skins.Count} skins ({ConfigManager.Controllers.Count} devices available)";
			int numFail = ConfigManager.Skins.Count(s => s.LoadResult != SkinLoadResult.Ok);
			if (numFail > 0)
				skinText += $" ({numFail} skins failed to load)";
			lblSkins.Text = skinText;
		}

		private static Image GetControllerImage(IController ctrlr) {
			if (ctrlr is ArduinoController) return Resources.arduino;
			else return GetControllerImage(ctrlr.Type);
		}

		private static Image GetControllerImage(ControllerType ctrlrType) {
			switch (ctrlrType) {
				case ControllerType.SNES:
					return Resources.snes;
				case ControllerType.N64:
					return Resources.n64;
				case ControllerType.NGC:
					return Resources.ngc;
				case ControllerType.PS2:
					return Resources.ps;
				case ControllerType.Unknown:
					return Resources.generic;
			}
			return null;
		}

		private static Image GetSkinImage(Skin skin) {
			if (skin is SvgSkin) return Resources.svg;
			else if (skin is NintendoSpySkin) return Resources.nspy;
			else if (skin is PadpyghtSkin) return Resources.padpy;
			else return null;
		}

		private void ActivateConfig(IController ctrlr, Skin skin) {
			if (skin?.LoadResult != SkinLoadResult.Ok) return;

			skin.Activate();
			// apply remap if it was previously selected
			if (skin is SvgSkin svg && ConfigManager.SelectedRemaps[svg] is ColorRemap rmp) {
				rmp.ApplyToSkin(svg);
			}
			else if (skin is NintendoSpySkin nspySkin && ConfigManager.SelectedNSpyBackgrounds.ContainsKey(nspySkin)) {
				nspySkin.SelectBackground(ConfigManager.SelectedNSpyBackgrounds[nspySkin]);
			}

			ConfigManager.ActiveSkin = skin;
			ConfigManager.SetActiveController(ctrlr);
			
			// find desired window size
			if (ConfigManager.WindowSizes.ContainsKey(skin)) {
				var wsz = ConfigManager.WindowSizes[skin];
				if (wsz != Size.Empty)
					this.Size = wsz - glControl.Size + this.Size;
				else
					ConfigManager.WindowSizes[skin] = glControl.Size;
			}
			ConfigManager.Save();
			UpdateController();
			Render();
		}

		private void glControl_Load(object sender, EventArgs e) {
			glControl.MakeCurrent();
			glControl.VSync = true;
		}

		private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
		private const int maxFPS = 60;
		long prevTicks = 0;
		private void OnApplicationOnIdle(object s, EventArgs a) {
			while (glControl.IsIdle) {
				glControl.MakeCurrent();
				_stopwatch.Restart();
				if (UpdateController() || Environment.TickCount - prevTicks > 200) {
					Render();
					prevTicks = Environment.TickCount; // Redraw at least every 200ms
				}
				Thread.Sleep((int)(Math.Max(1000f / maxFPS - _stopwatch.Elapsed.TotalMilliseconds, 0)));
			}
		}

		private bool UpdateController() {
			if (ConfigManager.ActiveSkin == null) return false;
			return ConfigManager.ActiveSkin.UpdateState(ConfigManager.GetActiveController());
		}


		private void Render() {
			glControl.MakeCurrent();
			GL.ClearColor(Color.FromArgb(0, ConfigManager.BackgroundColor));
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

			GL.MatrixMode(MatrixMode.Modelview);
			GL.LoadIdentity();

			GL.MatrixMode(MatrixMode.Projection);
			GL.LoadIdentity();
			GL.Ortho(0, glControl.Width, glControl.Height, 0, 0.0, 4.0);

			ConfigManager.ActiveSkin?.Render(glControl.Width, glControl.Height);
			glControl.SwapBuffers();
		}


		private void OnResize(object sender, EventArgs e) {
			if (ConfigManager.ActiveSkin != null) {
				ConfigManager.WindowSizes[ConfigManager.ActiveSkin] = glControl.Size;
				ConfigManager.ActiveSkin?.Render(glControl.Width, glControl.Height, true);
			}
			GL.Viewport(0, 0, glControl.Width, glControl.Height);
			Render();
		}

		private void tsmiSetWindowSize_Click(object sender, EventArgs e) {
			var frm = new WindowSizePicker(glControl.Size) {
				StartPosition = FormStartPosition.CenterParent
			};
			if (frm.ShowDialog() == DialogResult.OK) {
				this.Size = frm.ChosenSize - glControl.Size + this.Size;
				ConfigManager.Save();
			}
		}
		private void tsmiSkinFolders_Click(object sender, EventArgs e) {
			(new SkinFoldersForm(ConfigManager.SkinFolders)).ShowDialog(this);

			ConfigManager.Save();
			ReloadAll();
		}

		private void ReloadAll() {
			ConfigManager.Skins.Clear();
			ConfigManager.SkinFolders.Clear();
			ConfigManager.WindowSizes.Clear();
			ConfigManager.SelectedRemaps.Clear();
			ConfigManager.AvailableRemaps.Clear();
			ConfigManager.Load();
			BuildMenu();
			ActivateConfig(ConfigManager.GetActiveController(), ConfigManager.ActiveSkin);
		}

		private void tsmiAbout_Click(object sender, EventArgs e) {
			new AboutBox().Show(this);
		}

		private void OnDeviceArrival(object sender, UsbNotificationEventArgs args) {
			ScheduleBuildMenu();
			// see if this was our active controller and we can reactivate it
			var ac = ConfigManager.GetActiveController();
			if (string.Compare(ac?.DevicePath, args.Name, StringComparison.OrdinalIgnoreCase) == 0) {
				ac?.Activate();
			}
		}

		private void OnDeviceRemoval(object sender, UsbNotificationEventArgs args) {
			ScheduleBuildMenu();
		}

		#region update checking/performing

		private void tsmiCheckUpdates_Click(object sender, EventArgs e) {
			status.Visible = true;
			PerformUpdateCheck(true);
		}

		private void UpdateStatus(string text, int progressBarValue) {
			if (InvokeRequired) {
				Invoke((Action<string, int>)UpdateStatus, text, progressBarValue);
				return;
			}
			if (progressBarValue < pbProgress.Value && pbProgress.Value != 100) {
				return;
			}

			lblStatus.Text = "Status: " + text;
			if (progressBarValue < 100)
				// forces 'instant update'
				pbProgress.Value = progressBarValue + 1;
			pbProgress.Value = progressBarValue;
		}

		private void PerformUpdateCheck(bool msgBox = false) {
			var uc = new UpdateChecker();
			uc.AlreadyLatest += (o, e) => {
				if (msgBox) MessageBox.Show("You are already using the latest version available", "Already latest", MessageBoxButtons.OK, MessageBoxIcon.Information);
				UpdateStatus("already latest version", 100);
				Task.Delay(2000).ContinueWith(task => {
					if (InvokeRequired && IsHandleCreated) Invoke((Action)delegate { pbProgress.Visible = false; });
				});
			};
			uc.Connected += (o, e) => UpdateStatus("connected", 10);
			uc.DownloadProgressChanged += (o, e) => { /* care, xml is small anyway */
			};
			uc.UpdateCheckFailed += (o, e) => {
				UpdateStatus("update check failed", 100);
				if (msgBox) MessageBox.Show("Update check failed", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Task.Delay(2000).ContinueWith(task => Invoke((Action)delegate { pbProgress.Visible = false; }));
			};
			uc.UpdateAvailable += (o, e) => {
				UpdateStatus("update available", 100);

				var dr = MessageBox.Show($"An update to version {e.Version.ToString()} released on {e.ReleaseDate.ToShortDateString()} is available. Release notes: \r\n\r\n{e.ReleaseNotes}\r\n\r\nUpdate now?", "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
				if (dr == DialogResult.Yes)
					DownloadAndUpdate(e.DownloadUrl);
			};
			uc.CheckVersion();
		}

		private void DownloadAndUpdate(string url) {
			UpdateStatus("downloading new program version", 0);
			var wc = new WebClient();
			wc.Proxy = null;

			var address = new Uri(UpdateChecker.UpdateCheckHost + url);
			wc.DownloadProgressChanged += (sender, args) => BeginInvoke((Action)delegate { UpdateStatus($"downloading, {args.ProgressPercentage * 95 / 100}%", args.ProgressPercentage * 95 / 100); });

			wc.DownloadDataCompleted += (sender, args) => {
				UpdateStatus("download complete, running installer", 100);
				string appPath = Path.GetTempPath();
				string dest = Path.Combine(appPath, "MUNIA_update");

				int suffixNr = 0;
				while (File.Exists(dest + (suffixNr > 0 ? suffixNr.ToString() : "") + ".exe"))
					suffixNr++;

				dest += (suffixNr > 0 ? suffixNr.ToString() : "") + ".exe";
				File.WriteAllBytes(dest, args.Result);
				// invoke 
				var psi = new ProcessStartInfo(dest);
				psi.Arguments = "/Q";
				Process.Start(psi);
				Close();
			};

			// trigger it all
			wc.DownloadDataAsync(address);
		}

		#endregion

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
			ConfigManager.Save();
		}

		private void tsmiMuniaSettings_Click(object sender, EventArgs e) {
			var devs = MuniaController.GetConfigInterfaces().ToList();

			if (!devs.Any()) {
				MessageBox.Show("No config interfaces not found. This typically has one of three causes:\r\n  MUNIA isn't plugged in\r\n  MUNIA is currently in bootloader mode\r\n  Installed firmware doesn't implement this feature yet. Upgrade using the instructions at http://munia.io/fw",
					"Not possible", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			ConfigInterface intf = null;
			if (devs.Count() == 1) intf = devs.First();
			else {
				var pickerDlg = new DevicePicker(devs);
				if (pickerDlg.ShowDialog() == DialogResult.OK)
					intf = pickerDlg.ChosenDevice;
			}
			try {
				if (intf is MuniaConfigInterface mnci) {
					var dlg = new MuniaSettingsDialog(mnci);
					dlg.ShowDialog(this);
				}
				else if (intf is MusiaConfigInterface msci) {
					var dlg = new MusiaSettingsDialog(msci);
					dlg.ShowDialog(this);
				}
			}
			catch (InvalidOperationException exc) {
				MessageBox.Show(this, "An error occurred while retrieving information from the MUNIA device:\r\n\r\n",
					"Invalid error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			catch (TimeoutException exc) {
				MessageBox.Show(this, "A timeout occurred while retrieving information from the MUNIA device:\r\n\r\n",
					"Timeout", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			catch (Exception exc) {
				MessageBox.Show(this, "An unknown error occurred while retrieving information from the MUNIA device:\r\n\r\n" + exc.InnerException,
					"Unknown error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void tsmiFirmware_Click(object sender, EventArgs e) {
			var frm = new BootloaderForm {
				StartPosition = FormStartPosition.CenterParent
			};
			frm.ShowDialog(this);
		}

		private void tsmiMapArduinoDevices_Click(object sender, EventArgs args) {
			var frm = new ArduinoMapperForm(ConfigManager.ArduinoMapping) {
				StartPosition = FormStartPosition.CenterParent
			};
			if (frm.ShowDialog(this) == DialogResult.OK) {
				foreach (var e in frm.Mapping)
					ConfigManager.ArduinoMapping[e.Key] = e.Value;
				ConfigManager.LoadControllers();
				BuildMenu();
				ConfigManager.Save();
			}
		}

		private void tsmiSetLagCompensation_Click(object sender, EventArgs args) {
			var frm = new DelayValuePicker(ConfigManager.Delay) {
				StartPosition = FormStartPosition.CenterParent
			};
			if (frm.ShowDialog(this) == DialogResult.OK) {
				ConfigManager.Delay = frm.ChosenDelay;
				ConfigManager.Save();
			}
		}

		private void glControl_MouseClick(object sender, MouseEventArgs args) {
			if (args.Button == MouseButtons.Right) {
				popup.Show(glControl, args.Location);
			}
		}

		private void tsmiBackgroundChange_Click(object sender, EventArgs e) {
			var dlg = new ColorDialog2 { Color = ConfigManager.BackgroundColor };
			var colorBackup = ConfigManager.BackgroundColor;
			dlg.ColorChanged += (o, eventArgs) => {
				// retain transparency
				ConfigManager.BackgroundColor = Color.FromArgb(colorBackup.A, dlg.Color);
				Render();
			};
			if (dlg.ShowDialog(this) != DialogResult.OK) {
				ConfigManager.BackgroundColor = colorBackup;
			}
		}

		private void tsmiBackgroundTransparent_Click(object sender, EventArgs e) {
			// flip transparency
			ConfigManager.BackgroundColor = Color.FromArgb(255 - ConfigManager.BackgroundColor.A, ConfigManager.BackgroundColor);
			tsmiBackgroundTransparent.Checked = ConfigManager.BackgroundColor.A == 0;
			Render();
		}

		private void tsmiManageThemes_Click(object sender, EventArgs e) {
			if (ConfigManager.ActiveSkin is SvgSkin svg) {
				var managerForm = new RemapManagerForm(svg, ConfigManager.SelectedRemaps[svg],
					ConfigManager.AvailableRemaps[svg.Path]);

				managerForm.SelectedRemapChanged += (o, args) =>
					SelectRemap(managerForm.SelectedRemap);

				managerForm.ShowDialog();
				ConfigManager.Save();
				SelectRemap(managerForm.SelectedRemap);
			}
		}
		
		private void testControllerToolStripMenuItem_Click(object sender, EventArgs e) {
			new GamepadTester().ShowDialog();
		}

		private void popup_Opening(object sender, System.ComponentModel.CancelEventArgs e) {
			tsmiManageThemes.Enabled = ConfigManager.ActiveSkin is SvgSkin;

			// populate the available themes list
			tsmiApplyTheme.DropDownItems.Clear();
			if (ConfigManager.ActiveSkin is SvgSkin svg) {
				var remaps = ConfigManager.AvailableRemaps[svg.Path];
				tsmiApplyTheme.Enabled = remaps.Any(r=>!r.IsSkinDefault);

				var selectedRemap = ConfigManager.SelectedRemaps[svg];
				foreach (var remap in ConfigManager.AvailableRemaps[svg.Path]) {
					var tsmiSkin = new ToolStripMenuItem(remap.Name, null, (_,__) => SelectRemap(remap));

					// put a checkmark in front if this is the selected remap
					tsmiSkin.Checked = remap.Equals(selectedRemap);

					tsmiApplyTheme.DropDownItems.Add(tsmiSkin);
				}
				tsmiBackground.Visible = true;
			}
			else {
				tsmiApplyTheme.Enabled = false;
				tsmiBackground.Visible = false;
			}

			if (ConfigManager.ActiveSkin is NintendoSpySkin nspy) {
				tsmiBackground.Visible = false;
				tsmiBackgroundNSpy.Visible = true;
				tsmiBackgroundNSpy.DropDownItems.Clear();
				foreach (var bg in nspy.Backgrounds) {
					ToolStripMenuItem item = new ToolStripMenuItem(bg.Key);
					item.Click += (o, args) => nspy.SelectBackground(bg.Key);
					item.Checked = bg.Value == nspy.SelectedBackground;
					tsmiBackgroundNSpy.DropDownItems.Add(item);
				}
			}
			else {
				tsmiBackgroundNSpy.Visible = false;
			}
		}

		private void SelectRemap(ColorRemap remap) {
			if (remap != null && ConfigManager.ActiveSkin is SvgSkin svg) {
				svg.ApplyRemap(remap);
				ConfigManager.SelectedRemaps[svg] = remap;
				// force redraw of the base
				ConfigManager.ActiveSkin?.Render(glControl.Width, glControl.Height, true);
				Render();
			}
		}
	}

}