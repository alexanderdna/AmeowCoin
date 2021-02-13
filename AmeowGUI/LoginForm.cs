using System;
using System.Windows.Forms;

namespace Ameow.GUI
{
    public partial class LoginForm : Form
    {
        public enum Result
        {
            Closed,
            LoggedIn,
            CreatingWallet,
        }

        private App _app;

        public Result FormResult { get; private set; }

        public LoginForm(App app)
        {
            InitializeComponent();

            _app = app;
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            var passphrase = txtPassphrase.Text;
            if (_app.LoginWallet(passphrase) is true)
            {
                FormResult = Result.LoggedIn;
                Close();
            }
            else
            {
                MessageBox.Show("Invalid passphrase.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCreateNew_Click(object sender, EventArgs e)
        {
            FormResult = Result.CreatingWallet;
            Close();
        }
    }
}