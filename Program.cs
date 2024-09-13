using System.Text;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;

namespace ChatServerApp
{
    class Program
    {
        private static readonly ConcurrentDictionary<string, string> clients = new ConcurrentDictionary<string, string>();

        static async Task Main(string[] args)
        {
            using (var serverSocket = new RouterSocket("@tcp://localhost:5555"))
            {
                Console.WriteLine("Server started, waiting for clients...");
                while (true)
                {
                    try
                    {
                        var clientMessage = serverSocket.ReceiveMultipartMessage();
                        await HandleClientMessage(serverSocket, clientMessage);
                    }
                    catch (NetMQException ex)
                    {
                        Console.WriteLine($"An error occurred in socket operation: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Unexpected error: {ex.Message}");
                    }
                }
            }
        }

        private static async Task HandleClientMessage(RouterSocket serverSocket, NetMQMessage clientMessage)
        {
            var clientId = clientMessage[0].ConvertToString();
            var messageType = clientMessage[1].ConvertToString();
            var message = clientMessage[2].ConvertToString();

            Console.WriteLine($"Received message from client {clientId}: {messageType} - {message}");

            switch (messageType)
            {
                case "CONNECT":
                case "RECONNECT":
                    clients[message] = clientId;
                    Console.WriteLine($"{message} {(messageType == "CONNECT" ? "connected" : "reconnected")}.");
                    await BroadcastOnlineUsers(serverSocket);
                    break;

                case "DISCONNECT":
                    clients.TryRemove(message, out _);
                    Console.WriteLine($"{message} disconnected.");
                    await BroadcastOnlineUsers(serverSocket);
                    break;

                case "MESSAGE":
                    await HandleMessage(serverSocket, clientId, message);
                    break;

                case "PING":
                    Console.WriteLine($"Received PING from {clientId}, sending PONG.");
                    await SendPong(serverSocket, clientId);
                    break;

                default:
                    Console.WriteLine("Unknown message type.");
                    break;
            }
        }

        private static async Task HandleMessage(RouterSocket serverSocket, string clientId, string message)
        {
            var parts = message.Split(new[] { ':' }, 2);
            if (parts.Length != 2)
            {
                Console.WriteLine("Invalid message format.");
                await SendErrorResponse(serverSocket, clientId, "Invalid message format.");
                return;
            }

            var recipient = parts[0];
            var chatMessage = parts[1];

            if (!clients.TryGetValue(recipient, out var recipientId))
            {
                Console.WriteLine($"Recipient {recipient} not found.");
                await SendErrorResponse(serverSocket, clientId, $"Recipient {recipient} not found.");
                return;
            }

            if (recipientId == clientId)
            {
                Console.WriteLine($"Cannot route message: sender and recipient are the same ({clientId}).");
                await SendErrorResponse(serverSocket, clientId, "Cannot send a message to yourself.");
                return;
            }

            Console.WriteLine($"Routing message from {clientId} to {recipient}: {chatMessage}");
            await SendMessage(serverSocket, recipientId, clientId, chatMessage);
        }

        private static async Task SendMessage(RouterSocket serverSocket, string recipientId, string clientId, string chatMessage)
        {
            serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(recipientId))
                        .SendMoreFrame("MESSAGE")
                        .SendFrame($"{clientId}:{chatMessage}");
            await Task.CompletedTask;
        }

        private static async Task SendErrorResponse(RouterSocket serverSocket, string clientId, string errorMessage)
        {
            serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                        .SendMoreFrame("ERROR")
                        .SendFrame(errorMessage);
            await Task.CompletedTask;
        }

        private static async Task SendPong(RouterSocket serverSocket, string clientId)
        {
            serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                        .SendMoreFrame("PONG")
                        .SendFrame(string.Empty);
            await Task.CompletedTask;
        }

        private static async Task BroadcastOnlineUsers(RouterSocket serverSocket)
        {
            var onlineUsers = string.Join(",", clients.Keys);
            Console.WriteLine($"Broadcasting online users: {onlineUsers}");

            foreach (var clientId in clients.Values)
            {
                serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                            .SendMoreFrame("ONLINE_USERS")
                            .SendFrame(onlineUsers);
            }
            await Task.CompletedTask;
        }
    }
}