using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MUNIA.Controllers;

namespace MUNIA.Skinning {

	public abstract class Skin {
        // Get a handle to an application window.
        [DllImport("USER32.DLL", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        // Activate an application window.
        [DllImport("USER32.DLL")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

		public string Name { get; set; }
		public string Path { get; set; }
		public SkinLoadResult LoadResult { get; protected set; }
		public List<ControllerType> Controllers { get; } = new List<ControllerType>();
		public abstract Size DefaultSize { get; }

		public abstract void Render(int width, int height, bool force = false);

		public bool UpdateState(IController controller) {
			var oldState = State;
			State = controller?.GetState();
			return !Equals(oldState, State);
		}
		public void UpdateState(ControllerState state) {
			State = state;
		}
		protected ControllerState State;

        private bool PrevSplitButtonState = false;

        public void HandleSplit() {
            if (State == null) return;

            var splitButtonState = State.Buttons[3];
            if (splitButtonState != PrevSplitButtonState) {
                if (splitButtonState) {
                    // Send a series of key presses to the Calculator application.
                    // Get a handle to the Calculator application. The window class
                    // and window name were obtained using the Spy++ tool.
                    IntPtr livesplitHandle = FindWindow(null, "LiveSplit");

                    // Verify that Calculator is a running process.
                    if (livesplitHandle == IntPtr.Zero) {
                        MessageBox.Show("Livesplit is not running.");
                        return;
                    }

                    // Make Calculator the foreground application and send it 
                    // a set of calculations.
                    SetForegroundWindow(livesplitHandle);
                    SendKeys.Send("{F12}");
                }

                PrevSplitButtonState = splitButtonState;
            }
        }

		public abstract void Activate();
		public abstract void Deactivate();

		public static Skin Clone(Skin skin) {
			if (skin is SvgSkin svg) {
				var clone = new SvgSkin();
				clone.Load(svg.Path);
				return clone;
			}
			else if (skin is NintendoSpySkin nspy) {
				var clone = new NintendoSpySkin();
				clone.Load(nspy.Path);
				return clone;
			}
			else if (skin is PadpyghtSkin ppyght) {
				var clone = new PadpyghtSkin();
				clone.Load(ppyght.Path);
				return clone;
			}

			return null;
		}

		public abstract void GetNumberOfElements(out int numButtons, out int numAxes);

		public abstract bool GetElementsAtLocation(Point location, Size skinSize,
			List<ControllerMapping.Button> buttons, List<ControllerMapping.Axis[]> axes);
	}

	public enum SkinLoadResult {
		Fail,
		Ok,
	}
}
