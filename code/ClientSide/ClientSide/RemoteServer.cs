using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace ClientSide
{
    public class RemoteServer : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        int WSAECONNABORTED = 10053; //codice SocketException: "connessione interrotta"
        int WSAECONNRESET = 10054; //codice SocketException: "connessione chiusa dall'altro host"

        //binding stato del server
        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public Socket so { get; set; }
        public IPAddress ip { get; set; }
        private IPEndPoint remoteEP { get; set; }
        private int port { get; set; }
        private String statusValue;
        public ClipMngrClient cmc { get; set; }
        
        //getter e setter stato server
        public String status
        {
            get
            {
                return this.statusValue;
            }

            set
            {
                if (value != this.statusValue)
                {
                    this.statusValue = value;
                    NotifyPropertyChanged();
                }

            }

        }

        public class StateObject
        {
            // Client socket.
            public Socket workSocket = null;
            // Size of receive buffer.
            public const int BufferSize = 256;
            // Receive buffer.
            public byte[] buffer = new byte[BufferSize];
            // Received data string.
            public StringBuilder sb = new StringBuilder();
        }

        //evento che serve a notificare invio e ricezione files
        private  ManualResetEvent sendDone =
            new ManualResetEvent(true);
        private static ManualResetEvent receiveDone =
            new ManualResetEvent(false);

        private  String response = String.Empty;

        //costruttore remote server
        public RemoteServer(String ip, int port)
        {
            this.port = port;
            this.ip = IPAddress.Parse(ip);
            this.remoteEP = new IPEndPoint(this.ip, port);
            cmc = null;
            
        }

        //setta lo stato del server
        public void setStatus(String s)
        {
            if(this.status!="disconnected"){
                this.status = s;
            }
        }

        //manda il file al server
        public void SendClipOb(){
            if(cmc!=null)
                cmc.SendClipObject();
        }

        //procedura che avvia l'autenticazione
        public bool StartAuth(String password)
        {
            try
            {
                so = null;
                so = new Socket(AddressFamily.InterNetwork,
                                SocketType.Stream, ProtocolType.Tcp); ;
                so.NoDelay = true;

                byte[] msg = Encoding.ASCII.GetBytes(password);

                so.Connect(this.remoteEP);
                so.Send(msg);
                // Receive the response from the remote device.
                int bytesRec = so.Receive(msg);
                String rcv = System.Text.Encoding.ASCII.GetString(msg, 0, bytesRec);
                
                if (rcv.Contains("OK"))
                {
                    this.status = "active";
                    return true;
                }
                else
                {
                    System.Windows.MessageBox.Show("Connessione rifiutata dal server");
                    this.status = "error";
                    so.Shutdown(SocketShutdown.Both);
                    so.Disconnect(true);
                    GC.Collect();
                    return false;

                }
            }
            catch (SocketException soe)
            {
                System.Windows.MessageBox.Show("Server non trovato");
                so.Close();
                return false;
            }

        }

        //avvia la callback di ricezione file
        public void Receive()
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = so;

                // Begin receiving the data from the remote device.
                so.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                //è più intuitivo per l'utente non ricevere notifiche, l'utente riproverà
            }
        }

        //callback di ricezione file
        private  void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the state object and the client socket 
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket server = state.workSocket;

                // Read data from the remote device.
                int bytesRead = server.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // There might be more data, so store the data received so far.
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

                    // Get the rest of the data.
                    server.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    // All the data has arrived; put it in response.
                    if (state.sb.Length > 1)
                    {
                        response = state.sb.ToString();
                    }
                    // Signal that all bytes have been received.
                    receiveDone.Set();
                }
            }
            catch (Exception e)
            {
            }
        }

        //procedura che avvia la callback di invio file
        public void Send(String data)
        {
            if (this.status == "connected" || this.status == "active")
            {
                // Convert the string data to byte data using ASCII encoding.
                byte[] byteData = Encoding.ASCII.GetBytes(data);

                // Begin sending the data to the remote device.
                try
                {
                    if (!so.Connected)
                    {
                        if (this.status == "connected" || this.status == "active")
                        {
                            this.status = "disconnected";
                        }
                        //System.Windows.MessageBox.Show("Il server con cui si sta tentando di comunicare è stato chiuso");
                        return;
                    }
                    else
                    {
                        so.BeginSend(byteData, 0, byteData.Length, 0,
                            new AsyncCallback(SendCallback), so);
                    }
                }
                catch (SocketException se)
                {
                    if (se.ErrorCode == WSAECONNRESET || se.ErrorCode == WSAECONNABORTED)
                    {
                        System.Windows.MessageBox.Show("Il server con cui si sta tentando di comunicare è stato chiuso e/o la connessione è stata persa");
                        this.status = "disconnected";
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Collegamento interrotto");
                        this.status = "disconnected";
                    }
                }
                catch (Exception exc)
                {
                    System.Windows.MessageBox.Show("E' stato riscontrato un problema - La connessione con il server è stata interrotta");
                }

            }

        }

        //stessa cosa di sopra ma con valore di ritorno
        public bool SendAndVerify(String data)
        {
            if (this.status == "connected" || this.status == "active")
            {
                // Convert the string data to byte data using ASCII encoding.
                byte[] byteData = Encoding.ASCII.GetBytes(data);

                // Begin sending the data to the remote device.
                try
                {
                    if (!so.Connected)
                    {
                        if (this.status == "connected" || this.status == "active")
                        {
                            this.status = "disconnected";
                        }
                        System.Windows.MessageBox.Show("il server con cui si sta tentando di comunicare è stato chiuso");
                        
                        return false;
                    }
                    so.BeginSend(byteData, 0, byteData.Length, 0,
                        new AsyncCallback(SendCallback), so);
                   
                }
                catch (SocketException se)
                {
                    if (se.ErrorCode == 10054)
                    {
                        System.Windows.MessageBox.Show("Il server con cui si sta tentando di comunicare è stato chiuso");
                        this.status = "disconnected";
                    }
                    return false;
                }
                catch (Exception exc)
                {
                    System.Windows.MessageBox.Show("E' stato riscontrato un problema con il server - La connessione è stata interrotta");
                    return false;
                }

            }

            return true;

        }

        //callback di invio file
        private  void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket server = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = server.EndSend(ar);
                //System.Windows.MessageBox.Show("Sent {0} bytes to server.", bytesSent.ToString());
                // Signal that all bytes have been sent.
                sendDone.Set();
            }
            catch (Exception e)
            {
            }
        }

        //con questa funzione vengono interrotte le connessioni alle periferiche e alla clipboard
        public bool DisConnect()
        {
            sendDone.WaitOne();
            bool closed=false;
            if (cmc != null)
            {
                cmc.closeMngr(out closed);
            }

            if (closed || cmc == null)
            {
                cmc = null;
                so.Shutdown(SocketShutdown.Both);
                so.Close();
                this.status = "disconnected";
                return true;
            }
            else
                return false;
        }
    }
}
