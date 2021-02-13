using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Ameow.GUI
{
    public partial class IbdForm : Form
    {
        private App _app;

        public bool WillContinue { get; private set; }

        public IbdForm(App app)
        {
            InitializeComponent();

            _app = app;
        }

        private async void IbdForm_Load(object sender, EventArgs e)
        {
            lblStatus.Text = "Connecting to peers...";

            await Task.Delay(100);

            var result = _app.ConnectToPeers();
            if (result != App.ConnectPeersResult.Success)
            {
                var choice = MessageBox.Show("Cannot connect to peers. You may not have the latest blockchain. Would to like to continue?", "Connection failed",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                WillContinue = choice == DialogResult.Yes;
                Close();
                return;
            }

            if (_app.IsInIbd)
            {
                lblStatus.Text = "Downloading blocks...";
                tmrWaitForIbd.Enabled = true;
            }
        }

        private void tmrWaitForIbd_Tick(object sender, EventArgs e)
        {
            if (_app.IsInIbd is false)
            {
                tmrWaitForIbd.Enabled = false;

                if (_app.Daemon.CurrentIbdPhase == Network.InitialBlockDownload.Phase.Succeeded)
                {
                    WillContinue = true;
                }
                else
                {
                    var choice = MessageBox.Show("Cannot download blocks. You may not have the latest blockchain. Would to like to continue?", "Connection failed",
                       MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    WillContinue = choice == DialogResult.Yes;
                }
                Close();
            }
        }
    }
}