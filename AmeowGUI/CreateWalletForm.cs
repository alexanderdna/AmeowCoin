using System;
using System.Windows.Forms;

namespace Ameow.GUI
{
    public partial class CreateWalletForm : Form
    {
        private App _app;

        public bool WalletCreated { get; private set; }

        public CreateWalletForm(App app)
        {
            InitializeComponent();

            _app = app;
        }

        private void btnCreate_Click(object sender, EventArgs e)
        {
            var passphrase = txtPassphrase.Text;
            var passphraseConfirm = txtPassphraseConfirm.Text;
            if (passphraseConfirm != passphrase)
            {
                MessageBox.Show("Passphrase and re-entered passphrase are not matched.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _app.CreateWallet(passphrase);
            WalletCreated = true;

            MessageBox.Show("Wallet file created.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}