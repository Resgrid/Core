namespace OpenPop.Pop3
{
	/// <summary>
	/// Describes the authentication method to use when authenticating towards a POP3 server.
	/// </summary>
	public enum AuthenticationMethod
	{
		/// <summary>
		/// Authenticate using the UsernameAndPassword method.<br/>
		/// This will pass the username and password to the server in cleartext.<br/>
		/// <see cref="Apop"/> is more secure but might not be supported on a server.<br/>
		/// This method is not recommended. Use <see cref="Auto"/> instead.
		/// <br/>
		/// If SSL is used, there is no loss of security by using this authentication method.
		/// </summary>
		UsernameAndPassword,

		/// <summary>
		/// Authenticate using the Authenticated Post Office Protocol method, which is more secure then
		/// <see cref="UsernameAndPassword"/> since it is a request-response protocol where server checks if the
		///  client knows a shared secret, which is the password, without the password itself being transmitted.<br/>
		/// This authentication method uses MD5 under its hood.<br/>
		/// <br/>
		/// This authentication method is not supported by many servers.<br/>
		/// Choose this option if you want maximum security.
		/// </summary>
		Apop,

		/// <summary>
		/// This is the recomended method to authenticate with.<br/>
		/// If <see cref="Apop"/> is supported by the server, <see cref="Apop"/> is used for authentication.<br/>
		/// If <see cref="Apop"/> is not supported, Auto will fall back to <see cref="UsernameAndPassword"/> authentication.
		/// </summary>
		Auto,

		/// <summary>
		/// Logs in the the POP3 server using CRAM-MD5 authentication scheme.<br/>
		/// This in essence uses the MD5 hashing algorithm on the user password and a server challenge.
		/// </summary>
		CramMd5
	}
}