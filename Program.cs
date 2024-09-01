using System;
using System.Collections.Generic;
using System.Linq;
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
            var clients = new Dictionary<string, string>();



            using (var serverSocket = new RouterSocket("@tcp://localhost:5555"))

            {

                Console.WriteLine("Server started, waiting for clients...");



                while (true)

                {

                    var clientMessage = serverSocket.ReceiveMultipartMessage();

                    var clientIdFrame = clientMessage[0];

                    var clientId = clientIdFrame.ConvertToString();

                    var messageType = clientMessage[1].ConvertToString();

                    var message = clientMessage[2].ConvertToString();



                    Console.WriteLine($"Received message from client {clientId}: {messageType} - {message}");



                    if (messageType == "CONNECT")

                    {

                        clients[message] = clientId;

                        Console.WriteLine($"{message} connected.");

                        BroadcastOnlineUsers(serverSocket, clients);

                    }

                    else if (messageType == "DISCONNECT")

                    {

                        clients.Remove(message);

                        Console.WriteLine($"{message} disconnected.");

                        BroadcastOnlineUsers(serverSocket, clients);

                    }
                    else if (messageType == "MESSAGE")
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
                                    serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                                                .SendMoreFrame("ERROR")
                                                .SendFrame("Cannot send a message to yourself.");
                                }
                                else
                                {
                                    Console.WriteLine($"Routing message from {clientId} to {recipient}: {chatMessage}");
                                    serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(recipientId))
                                                .SendMoreFrame("MESSAGE")
                                                .SendFrame($"{clientId}:{chatMessage}");
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
                    else if (messageType == "PING")
                    {
                        Console.WriteLine($"Received PING from {clientId}, sending PONG and OFFLINE_USERS.");

                        // Send PONG response
                        serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                                    .SendMoreFrame("PONG")
                                    .SendFrame(string.Empty);

                    }
                    else if (messageType == "REQUEST_ONLINE_USERS")
                    {
                        Console.WriteLine($"Received REQUEST_ONLINE_USERS from {clientId}, sending ONLINE_USERS.");
                        BroadcastOnlineUsers(serverSocket,clients);
                    }
                    else if (messageType == "REQUEST_OFFLINE_USERS")
                    {
                        Console.WriteLine($"Received REQUEST_OFFLINE_USERS from {clientId}, sending OFFLINE_USERS.");
                        BroadcastOfflineUsers(serverSocket);
                    }
                    else if (messageType == "UPDATE_OFFLINE_USERS")
                    {
                        var updatedOfflineUsers = message.Split(',');
                        UpdateOfflineUsers(updatedOfflineUsers);
                        Console.WriteLine("Received and updated offline users list from client.");
                        BroadcastOfflineUsers(serverSocket);
                    }
                }
            }
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

        private static void BroadcastOfflineUsers(RouterSocket serverSocket)
        {
            var offlineUsersList = string.Join(",", offlineUsers);
            Console.WriteLine($"Broadcasting offline users: {offlineUsersList}");

            foreach (var clientId in clients.Values)
            {
                serverSocket.SendMoreFrame(Encoding.UTF8.GetBytes(clientId))
                            .SendMoreFrame("OFFLINE_USERS")
                            .SendFrame(offlineUsersList);
            }
        }

        private static void UpdateOfflineUsers(string[] updatedOfflineUsers)
        {
            offlineUsers.Clear();
            foreach (var user in updatedOfflineUsers)
            {
                if (!string.IsNullOrWhiteSpace(user))
                {
                    offlineUsers.Add(user);
                }
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
