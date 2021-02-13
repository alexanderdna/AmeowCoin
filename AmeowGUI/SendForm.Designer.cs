
namespace Ameow.GUI
{
    partial class SendForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.btnSend = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.tmrMining = new System.Windows.Forms.Timer(this.components);
            this.lblHashrate = new System.Windows.Forms.Label();
            this.txtRecipientAddress = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.numAmount = new System.Windows.Forms.NumericUpDown();
            this.txtMaxSendable = new System.Windows.Forms.TextBox();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.numAmount)).BeginInit();
            this.SuspendLayout();
            // 
            // btnSend
            // 
            this.btnSend.Location = new System.Drawing.Point(155, 83);
            this.btnSend.Margin = new System.Windows.Forms.Padding(2, 3, 2, 3);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(157, 38);
            this.btnSend.TabIndex = 4;
            this.btnSend.Text = "SEND";
            this.btnSend.Click += new System.EventHandler(this.btnSend_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 50);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(69, 21);
            this.label1.TabIndex = 2;
            this.label1.Text = "Amount:";
            // 
            // tmrMining
            // 
            this.tmrMining.Interval = 500;
            // 
            // lblHashrate
            // 
            this.lblHashrate.AutoSize = true;
            this.lblHashrate.Location = new System.Drawing.Point(432, 92);
            this.lblHashrate.Name = "lblHashrate";
            this.lblHashrate.Size = new System.Drawing.Size(0, 21);
            this.lblHashrate.TabIndex = 13;
            // 
            // txtRecipientAddress
            // 
            this.txtRecipientAddress.Location = new System.Drawing.Point(155, 12);
            this.txtRecipientAddress.Name = "txtRecipientAddress";
            this.txtRecipientAddress.Size = new System.Drawing.Size(421, 29);
            this.txtRecipientAddress.TabIndex = 1;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(12, 15);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(135, 21);
            this.label5.TabIndex = 0;
            this.label5.Text = "Recipient address:";
            // 
            // numAmount
            // 
            this.numAmount.DecimalPlaces = 8;
            this.numAmount.Increment = new decimal(new int[] {
            1,
            0,
            0,
            524288});
            this.numAmount.Location = new System.Drawing.Point(155, 48);
            this.numAmount.Name = "numAmount";
            this.numAmount.Size = new System.Drawing.Size(248, 29);
            this.numAmount.TabIndex = 3;
            this.numAmount.ThousandsSeparator = true;
            // 
            // txtMaxSendable
            // 
            this.txtMaxSendable.Location = new System.Drawing.Point(409, 47);
            this.txtMaxSendable.Name = "txtMaxSendable";
            this.txtMaxSendable.ReadOnly = true;
            this.txtMaxSendable.Size = new System.Drawing.Size(167, 29);
            this.txtMaxSendable.TabIndex = 14;
            this.txtMaxSendable.TabStop = false;
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(419, 83);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(2, 3, 2, 3);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(157, 38);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "CANCEL";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // SendForm
            // 
            this.AcceptButton = this.btnSend;
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 21F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(588, 133);
            this.Controls.Add(this.txtMaxSendable);
            this.Controls.Add(this.numAmount);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSend);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblHashrate);
            this.Controls.Add(this.txtRecipientAddress);
            this.Controls.Add(this.label5);
            this.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.Name = "SendForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Send coins to an address";
            this.Load += new System.EventHandler(this.SendForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numAmount)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Timer tmrMining;
        private System.Windows.Forms.Label lblHashrate;
        private System.Windows.Forms.TextBox txtRecipientAddress;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.NumericUpDown numAmount;
        private System.Windows.Forms.TextBox txtMaxSendable;
        private System.Windows.Forms.Button btnCancel;
    }
}