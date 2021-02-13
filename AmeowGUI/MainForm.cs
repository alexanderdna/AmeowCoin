using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Ameow.GUI
{
    public partial class MainForm : Form
    {
        private App _app;

        public MainForm(App app)
        {
            InitializeComponent();

            _app = app;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            txtAddress.Text = _app.WalletAddress;

            updateBalance();

            _app.OnRelatedTransactionsReceived -= updateBalanceThreadSafe;
            _app.OnRelatedTransactionsReceived += updateBalanceThreadSafe;
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            var sendForm = new SendForm(_app);
            sendForm.ShowDialog();

            updateBalance();
        }

        private async void btnMine_Click(object sender, EventArgs e)
        {
            tmrMining.Enabled = true;
            btnSend.Enabled = false;
            btnMine.Enabled = false;

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            try
            {
                _app.Mine();
            }
            catch (App.ChainMutexLockedException)
            {
                MessageBox.Show("Chain is being locked. Please wait a moment.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Error);

                btnSend.Enabled = true;
                btnMine.Enabled = true;
                lblHashrate.Text = string.Empty;
                tmrMining.Enabled = false;
                return;
            }
            catch (App.NotValidTimeForNewBlockException ex)
            {
                MessageBox.Show(string.Format("Can only mine new block in {0}.", Utils.TimeUtils.TimeStringFromSeconds(ex.RemainingMilliseconds / 1000.0)),
                    "Wait", MessageBoxButtons.OK, MessageBoxIcon.Error);

                btnSend.Enabled = true;
                btnMine.Enabled = true;
                lblHashrate.Text = string.Empty;
                tmrMining.Enabled = false;
                return;
            }

            await Task.Run(() =>
            {
                while (_app.IsMining) Thread.Sleep(500);
            });

            sw.Stop();

            btnSend.Enabled = true;
            btnMine.Enabled = true;
            lblHashrate.Text = string.Empty;
            tmrMining.Enabled = false;

            if (_app.LastMiningSuccessful)
            {
                updateBalance();

                var duration = Utils.TimeUtils.TimeStringFromSeconds(sw.ElapsedMilliseconds / 1000.0);
                MessageBox.Show($"New block found after {duration}.", "Yesss!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Mining failed. Maybe someone was quicker.", "Awww!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void updateBalanceThreadSafe()
        {
            Invoke(new Action(updateBalance));
        }

        private void updateBalance()
        {
            (long usable, long pending) = _app.GetUnspentAmountInNekoshi();
            txtBalance.Text = string.Format("{0:N8} AMEOW", usable / (double)Config.NekoshiPerCoin);
            txtPendingBalance.Text = string.Format("{0:N8} AMEOW pending in transactions", pending / (double)Config.NekoshiPerCoin);
        }

        private void tmrMining_Tick(object sender, EventArgs e)
        {
            if (_app.IsMining)
            {
                lblHashrate.Text = string.Format("{0:N2} KH/s", _app.MiningHashrate / 1000);
            }
        }
    }
}