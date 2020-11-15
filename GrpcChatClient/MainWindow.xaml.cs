﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Grpc.Core;
using GrpcServer;

namespace GrpcChatClient
{
	/// <summary>
	///     Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly CancellationTokenSource cancellationTokenSource;
		private Guid userId = Guid.NewGuid();
		private AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse> call;

		public MainWindow()
		{
			// With help from http://www.networkcomms.net/creating-a-wpf-chat-client-server-application/
			// They used ProtoContract and ProtoMember as attribute.. 5 years ago.. very funny :)

			// Fix for current bug in wpf .net core. crashes with german localisation "BILDAUF" not found :)
			// var culture = new System.Globalization.CultureInfo("en-US");
			// Thread.CurrentThread.CurrentCulture = culture;
			// Thread.CurrentThread.CurrentUICulture = culture;

			cancellationTokenSource = new CancellationTokenSource();

			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

			InitializeComponent();
		}

		private async void Window_Loaded(object sender, RoutedEventArgs e)
		{
			try
			{
				call = await ConnectToServer();
				await Login(call, "Max");
				await ReceivingResponses(call);
			}
			catch (Exception exception)
			{
				await AppendLineToChatBox(exception.ToString());
			}
		}

		public async Task<AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse>> ConnectToServer()
		{
			await AppendLineToChatBox($"Trying to connect to server...");
			var client = ProtoClient.GrpcChatClientProvider.Create();
			call = client.SendMessages(cancellationToken: cancellationTokenSource.Token);
			return call;
		}

		private async Task ReceivingResponses(AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse> call)
		{
			IAsyncEnumerable<ChatMessagesResponse> responses = call.ResponseStream.ReadAllAsync(cancellationTokenSource.Token);
			await OutputResponses(responses);
		}

		public async Task Login(AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse> call, string name)
		{
			var request = new ChatMessagesRequest()
			{
				NewUser = new NewUserRequest()
				{
					Id = Guid.NewGuid().ToString(),
					Name = name
				}
			};

			await call.RequestStream.WriteAsync(request);

			await AppendLineToChatBox($"You joined the chat.");
		}

		public async Task SendMessageOverTheWire(string message)
		{
			var request = new ChatMessagesRequest()
			{
				ChatMessage = new ChatMessageRequest()
				{
					Id = userId.ToString(),
					Message = message
				}
			};

			await call.RequestStream.WriteAsync(request);
		}

		private async Task OutputResponses(IAsyncEnumerable<ChatMessagesResponse> responses)
		{
			await foreach (var response in responses)
			{
				switch (response.MessagesCase)
				{
					case ChatMessagesResponse.MessagesOneofCase.None:
						break;
					case ChatMessagesResponse.MessagesOneofCase.ChatMessage:
						await AppendLineToChatBox($"[{response.ChatMessage.UserName}]: {response.ChatMessage.Message}");
						break;
					case ChatMessagesResponse.MessagesOneofCase.NewUser:
						await AppendLineToChatBox($"[{response.NewUser.Name}] connected.");
						break;
				}
			}
		}

		/// <summary>
		///     Append the provided message to the chatBox text box.
		/// </summary>
		/// <param name="message"></param>
		public async Task AppendLineToChatBox(string message)
		{
			//To ensure we can successfully append to the text box from any thread
			//we need to wrap the append within an invoke action.
			await chatBox.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
			{
				chatBox.AppendText(messageToAdd + Environment.NewLine);
				chatBox.ScrollToEnd();
			}), new object[] {message});
		}

		/// <summary>
		///     Refresh the userlist
		/// </summary>
		private async Task UpdateUserList()
		{
			await this.userList.Dispatcher.BeginInvoke(new Action<string[]>((users) =>
			{
				//First clear the text box
				userList.Text = "";

				//Now write out each username
				foreach (var username in users)
					userList.AppendText(username + "\n");
			}), new object[] {userList});
		}

		/// <summary>
		///     Send any entered message when we click the send button.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		/// Event.. no Task as return value..
		private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
		{
			await SendMessage();
		}

		/// <summary>
		///     Send any entered message when we press enter or return
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		/// Event.. no Task as return value..
		private async void MessageText_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter || e.Key == Key.Return)
			{
				await SendMessage();
			}
		}

		/// <summary>
		///     Correctly shutdown NetworkComms .Net when closing the WPF application
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			await Disconnect();
		}

		private async Task Disconnect()
		{
			await AppendLineToChatBox($"Disconnecting...");

			cancellationTokenSource.Cancel();

			if (call != null)
			{
				await call.RequestStream.CompleteAsync();
				call.Dispose();
				call = null;
			}

			cancellationTokenSource.Dispose();
		}

		/// <summary>
		///     Send our message.
		/// </summary>
		private async Task SendMessage()
		{
			// If we have tried to send a zero length string we just return
			if (messageText.Text == string.Empty)
			{
				return;
			}

			await SendMessageOverTheWire(messageText.Text);

			messageText.Clear();
		}
	}
}