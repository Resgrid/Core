using System.Windows.Forms;

namespace OpenPop.TestApplication
{
	/// <summary>
	/// A form which can simply show a text string - the source of an email
	/// </summary>
	public class ShowSourceForm : Form
	{
		private readonly string Source;

		private Panel mainPanel;
		private RichTextBox sourceText;

		/// <summary>
		/// Constructs a ShowSourceForm with the <paramref name="source"/> text to use.
		/// </summary>
		/// <param name="source">The text to show to the user</param>
		public ShowSourceForm(string source)
		{
			Source = source;
			InitializeComponent();
			SetupText();
		}

		private void SetupText()
		{
			sourceText.Text = Source;
		}

		#region Windows Form Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.sourceText = new System.Windows.Forms.RichTextBox();
			this.mainPanel = new System.Windows.Forms.Panel();
			this.mainPanel.SuspendLayout();
			this.SuspendLayout();
			// 
			// sourceText
			// 
			this.sourceText.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.sourceText.Location = new System.Drawing.Point(3, 3);
			this.sourceText.Name = "sourceText";
			this.sourceText.ReadOnly = true;
			this.sourceText.Size = new System.Drawing.Size(281, 257);
			this.sourceText.TabIndex = 1;
			this.sourceText.Text = "";
			this.sourceText.WordWrap = false;
			// 
			// mainPanel
			// 
			this.mainPanel.Controls.Add(this.sourceText);
			this.mainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.mainPanel.Location = new System.Drawing.Point(0, 0);
			this.mainPanel.Name = "mainPanel";
			this.mainPanel.Size = new System.Drawing.Size(284, 262);
			this.mainPanel.TabIndex = 2;
			// 
			// ShowSourceForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(284, 262);
			this.Controls.Add(this.mainPanel);
			this.Name = "ShowSourceForm";
			this.Text = "Source (Using US-ASCII encoding)";
			this.mainPanel.ResumeLayout(false);
			this.ResumeLayout(false);
		}
		#endregion
	}
}