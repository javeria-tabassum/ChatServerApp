using System.Text;
using NetMQ;
using NetMQ.Sockets;

namespace ChatServerApp
{
    class Program
    {
        private static readonly Dictionary<string, string> clients = new Dictionary<string, string>();
        private static readonly HashSet<string> offlineUsers = new HashSet<string>();

        static void Main(string[] args)
        {
            using (var serverSocket = new RouterSocket("@tcp://localhost:5555"))
            {
                Console.WriteLine("Server started, waiting for clients...");

                while (true)
                {
                    try
                    {
                        var clientMessage = serverSocket.ReceiveMultipartMessage();
                        HandleClientMessage(serverSocket, clientMessage);
                    }
                    catch (NetMQException ex)
                    {
                        Console.WriteLine($"An error occurred in socket operation: {ex.Message}");
                    }
                }
            }
        }

        private static void HandleClientMessage(RouterSocket serverSocket, NetMQMessage clientMessage)
        {
            var clientIdFrame = clientMessage[0];
            var clientId = clientIdFrame.ConvertToString();
            var messageType = clientMessage[1].ConvertToString();
            var message = clientMessage[2].ConvertToString();

            Console.WriteLine($"Received message from client {clientId}: {messageType} - {message}");

            switch (messageType)
            {
                case "CONNECT":
                    clients[message] = clientId;
                    Console.WriteLine($"{message} connected.");
                    BroadcastOnlineUsers(serverSocket, clients);
                    break;

                case "DISCONNECT":
                    clients.Remove(message);
                    Console.WriteLine($"{message} disconnected.");
                    BroadcastOnlineUsers(serverSocket, clients);
                    break;

                case "MESSAGE":
                    HandleMessage(serverSocket, clientId, message);
                    break;

                case "PING":
                    Console.WriteLine($"Received PING from {clientId}, sending PONG.");
                    SendPong(serverSocket, clientId);
                    break;

                case "RECONNECTED":
                    clients[message] = clientId;
                    Console.WriteLine($"{message} reconnected.");
                    BroadcastOnlineUsers(serverSocket, clients);
                    break;

                default:
                    Console.WriteLine("Unknown message type.");
                    break;
            }
        }

        private static void HandleMessage(RouterSocket serverSocket, string clientId, string message)
        {
            var parts = message.Split(new[] { ':' }, 2);
            if (parts.Length == 2)
            {
                var recipient = parts[0];
                var chatMessage = parts[1];

                if (clients.TryGetValue(recipient, out var recipientId))
                {
                    if (recipientId == clientId)
                    {
                        Console.WriteLine($"Cannot route message: sender and recipient are the same ({clientId}).");
                        SendErrorResponse(serverSocket, clientId, "Cannot send a message to yourself.");
                    }
                    else
                    {
                        Console.WriteLine($"Routing message from {clientId} to {recipient}: {chatMessage}");
                        SendMessage(serverSocket, recipientId, clientId, chatMessage);
                    }
                }
                else
                {
                    Console.WriteLine($"Recipient {recipient} not found.");
                }
            }
            else
            {
                Console.WriteLine("Invalid message format.");
            }
        }

        private static void SendMessage(RouterSocket serverSocket, string recipientId, string clientId, string chatMessage)
        {        
                serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(recipientId))
                            .SendMoreFrame("MESSAGE")
                            .SendFrame($"{clientId}:{chatMessage}");

        }

        private static void SendErrorResponse(RouterSocket serverSocket, string clientId, string errorMessage)
        {
                serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                            .SendMoreFrame("ERROR")
                            .SendFrame(errorMessage);
        }

        private static void SendPong(RouterSocket serverSocket, string clientId)
        {
                Console.WriteLine($"Received PING from {clientId}, sending PONG and OFFLINE_USERS.");

                serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                            .SendMoreFrame("PONG")
                            .SendFrame(string.Empty);
        }

        private static void BroadcastOnlineUsers(RouterSocket serverSocket, Dictionary<string, string> clients)
        {
            var onlineUsers = string.Join(",", clients.Keys);
                Console.WriteLine($"Broadcasting online users: {onlineUsers}");

                foreach (var clientId in clients.Values)
                {
                    serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                                .SendMoreFrame("ONLINE_USERS")
                                .SendFrame(onlineUsers);
                }
        }

        private static string GetUsernameById(string clientId)
        {
            foreach (var entry in clients)
                {
                    if (entry.Value == clientId)
                        return entry.Key;
                }
            return null;
        }
    }
}
