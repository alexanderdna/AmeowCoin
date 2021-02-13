using System;
using System.Windows.Forms;

namespace Ameow.GUI
{
    public partial class SendForm : Form
    {
        private App _app;

        public SendForm(App app)
        {
            InitializeComponent();

            _app = app;
        }

        private void SendForm_Load(object sender, EventArgs e)
        {
            numAmount.Minimum = 0;
            numAmount.Maximum = _app.GetUnspentAmountInNekoshi().usable / (decimal)Config.NekoshiPerCoin;
            txtMaxSendable.Text = string.Format("/{0:N8}", numAmount.Maximum);
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            string recipientAddress = txtRecipientAddress.Text.Trim();
            long amountInNekoshi = (long)numAmount.Value * Config.NekoshiPerCoin;

            if (recipientAddress == _app.WalletAddress)
            {
                showError("Your are trying to send coins to yourself.");
                return;
            }

            if (Utils.AddressUtils.VerifyAddress(recipientAddress) is false)
            {
                showError("Recipient address seems to be invalid.");
                return;
            }

            if (amountInNekoshi is < Config.FeeNekoshiPerTx or > Config.MaxSendableNekoshi)
            {
                showError("Amount is too low or too high.");
                return;
            }

            var sendResult = _app.Send(recipientAddress, amountInNekoshi);
            if (sendResult.Error == ChainManager.SendResult.ErrorType.None)
            {
                MessageBox.Show("Coins sent. Transaction will be confirmed in the next blocks.",
                    "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);

                Close();
            }
            else
            {
                showError("Cannot send coins, error: " + sendResult.Error);
            }
        }

        private static void showError(string error)
        {
            MessageBox.Show(error, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}