using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using MahApps.Metro.Controls.Dialogs;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.IO;

namespace ServerSide
{
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>

    public partial class MainWindow
    {

        public static TcpClient client;
        public static TcpListener server;
        public ClipMngrClient clipclient;
        static String Pass;
        static bool rejected;
        static bool flag;
        public static bool active;
        public static String stato;
        public WindowRedBorder wRB;

        public static ManualResetEvent tcpClientConnected = new ManualResetEvent(false);
        public static double clientWidth;
        public static double clientHeight;
        public static double thisWid;
        public static double thisHei;

        [DllImport("user32.dll")]
        static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);
        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint idThread);
        [DllImport("user32.dll")]
        static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr t);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, int dwExtraInfo);
        [DllImport("user32.dll")]
        static extern bool keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SetClipboardViewer(IntPtr hWndNewViewer);
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int MOUSEEVENTF_HWHEEL = 0x01000;
        public const int KEYEVENTF_KEYUP = 0x0002;
        public const int WM_DRAWCLIPBOARD = 0x0308;
        public const int WM_MBUTTONUP = 0x040;
        public const int WM_MBUTTONDOWN = 0x020;
        TaskbarIcon tb;
        public bool onetime = true;
        HwndSource source;

        //FileStream flog;
        //StreamWriter sw;

        public MainWindow()
        {
            
            //inizializza un log sul desktop
            //FileInfo f = new FileInfo(@"C:\Users\alex\Desktop\logfile.txt");
            //if (f.Exists)
            //{
            //    File.Delete(@"C:\Users\alex\Desktop\logfile.txt");
            //}
            
            //flog = new FileStream(@"C:\Users\alex\Desktop\logfile.txt", FileMode.OpenOrCreate);
            //sw = new StreamWriter(flog);

            InitializeComponent();
            thisWid= System.Windows.SystemParameters.PrimaryScreenWidth;
            thisHei = System.Windows.SystemParameters.PrimaryScreenHeight;
            tblk_clip.DataContext = clipclient;
            rejected = false;
            flag = false;
            active = false;

           
            //inizializziamo finestra per bordo rosso    
            wRB = new WindowRedBorder();
            wRB.ShowInTaskbar = false;

            //settings della tray icon
            tb = (TaskbarIcon)FindResource("TbIcon");
            tb.Icon = ServerSide.Properties.Resources.IconTask;
            tb.Visibility = System.Windows.Visibility.Hidden;
            this.SourceInitialized += new EventHandler(OnSourceInitialized);

           //otteniamo indirizzi del pc
            IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

            foreach (var it in localIPs)
            {
                if (it.AddressFamily == AddressFamily.InterNetwork)
                {
                    listenintf.Items.Add(it.ToString());
                }
            }
            listenintf.Items.Add("127.0.0.1"); //per debug con indirizzo errato
            listenintf.SelectedIndex = listenintf.Items.Count > 0 ? 0 : -1;//se non ci sono elementi il selected è vuoto (indice -1)
            
            server = null;
           
        }

        protected void OnSourceInitialized(object sender, EventArgs e)    
        { //questa funzione serve per far interagire WPF con Win32 (altrimenti non è possibile monitorare l'uso della clipboard)
            //si aggancia alla funzione di hook della clipboard di sistema
            WindowInteropHelper wih = new WindowInteropHelper(this);
            source = HwndSource.FromHwnd(wih.Handle);
            source.AddHook(WndProc);
            SetClipboardViewer(source.Handle);  //questa è una DLL import che fa sì che la nostra finestra si registri come "clipboardviewer"
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            /*sta in ascolto dei messaggi di sistema e appena viene ricevuto il messaggio WM_DRAWCLIPBOARD 
             * significa che il contenuto della clipboard è cambiato quindi viene automaticamente inviato al client se il server è
             * in quel momento attivo.
            */
            if(active && msg == WM_DRAWCLIPBOARD)
            {
                try
                {
                    clipclient.SendClipObject();
                }
                catch
                {
                    this.ShowMessageAsync("", "La clipboard non risulta connessa");
                }
            }
            return IntPtr.Zero;
        }

        private void Button_Click(object sender, RoutedEventArgs e) //bottone per ascolto in attesa del client
        {
            IPAddress localAddr;
            Int32 pr;
            bool verIP;
            if (listenintf.SelectedItem!=null)
            {
                verIP = IPAddress.TryParse(listenintf.SelectedItem.ToString(), out localAddr);
            }
            else
            {
                localAddr = new IPAddress(123445);
                verIP = false;
            }

            bool verPs = mypwIns.Password.ToString().Length > 1 && mypwIns.Password.ToString() != "";
            bool verPor = int.TryParse(myprIde.Text.ToString(), out pr);

            if (verIP && verPs && verPor)
            {
                Pass = mypwIns.Password.ToString();
            }
            else
            {
                String ls = "";
                int cnt = 0;
                if (!verIP)
                {
                    cnt++;
                    ls = ls + " IP";
                } if (!verPs)
                {
                    if(cnt>0){
                        ls =ls+ ",".ToString();
                    }
                    cnt++;
                    ls = ls + " Password";
                } if (!verPor)
                {
                    if (cnt>0)
                    {
                        ls =ls+ ",";
                    }
                    cnt++;
                    ls = ls + " Porta";
                }
                if (cnt > 1)
                {
                    this.ShowMessageAsync("Inserimento errato","i campi:" + ls + " sono errati");
                }
                else
                {
                   this.ShowMessageAsync("Inserimento errato", "il campo" + ls + " è errato");
                }
                return;
            }

            if (server != null && client != null)
            {
                this.ShowMessageAsync("Connessione già effettuata", "Sei già connesso!");
                if (rejected == true)
                {
                    rejected = !rejected;
                }

            }
            else if (client == null)
            {
                if (server == null || rejected == true)
                {
                    try
                    {
                        if (rejected == true) rejected = !rejected;
                        if (server != null)
                        {
                            server.Stop();
                            server = null;
                        }

                        tcpClientConnected.Reset();
                        server = new TcpListener(localAddr, pr);
                        server.ExclusiveAddressUse = false;

                        server.Start();

                        server.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTcpClientCallback), server);
                        
                    }
                    catch (InvalidOperationException ioe)
                    {
                        System.Windows.MessageBox.Show(ioe.ToString());
                    }
                    catch (Exception){
                        server.Stop();
                        server = null;
                        System.Windows.MessageBox.Show("Errore nel collegamento: l'indirizzo o la porta potrebbero essere già in uso");
                    }

                }
                else
                {
                    this.ShowMessageAsync("", "Sei già in attesa di una connessione");
                    if (rejected == true)
                    {
                        rejected = !rejected;
                    }
                }

            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e) //bottone interrompi per chiudere connessione o attesa
        {
            
            if (server == null)
            {
                 this.ShowMessageAsync("", "Nessuna connessione attiva"/*,message,mds*/);
                return;
            }
            else
            {
                if (client != null)
                {

                    if (System.Windows.MessageBox.Show("Stai tentando di interrompere il programma mentre un client è attivo, Procedere?", "Client connesso", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Stop) == System.Windows.MessageBoxResult.Yes)
                    {
                        bool closed=false;
                        if(clipclient!=null) clipclient.closeMngr(out closed);
                        if(closed){
                            clipclient = null;
                            flag = true;
                            active = false;
                            client.GetStream().Close();
                            client.Close();
                            client = null;
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("Connessione non chiusa poichè vi era un invio in corso");
                        }
                    }

                }
                else
                {
                    server.Stop();
                    server = null;
                }
            }

        }

        public void DoAcceptTcpClientCallback(IAsyncResult ar)
        {
            TcpListener listener = (TcpListener)ar.AsyncState;

            Regex R3 = new Regex("\\{.*?\\}");  //regex per dimensione schermo
            Regex R5 = new Regex(@"\([0-9]+\;[0-9]+\)"); //regex per posizione del mouse
            Regex R = new Regex(@"\([a-zA-Z]+\)");   //regex per pressione tasti mouse
            Regex R2 = new Regex("\\[.*?\\]");  //regex per pressione tasti tastiera
            Regex R4 = new Regex(@"\<(.*?)\>"); //regex per rilascio tasti tastiera
            int myx, myy;

            if (listener.Server.IsBound)
            {
                try
                {
                client = listener.EndAcceptTcpClient(ar);
                }
                catch (Exception e)
                {
                    return;
                }

                client.NoDelay = true;//disabilita algoritmo di Nagle per maggiore fluidità
                
                String data;
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[256];

                int i;

                //autenticazione
                while (true)
                {
                    i = stream.Read(buffer, 0, buffer.Length);
                    data = System.Text.Encoding.ASCII.GetString(buffer, 0, i);

                    // Processiamo i dati inviati dal client
                    if (data.ToString() == Pass)
                    {
                        byte[] msg = System.Text.Encoding.ASCII.GetBytes("OK!");
                        stream.Write(msg, 0, msg.Length);
                        active = true;
                        
                        //STABILITA CONNESSIONE: POSSO AVVIARE IL CLIENT DELLA CLIPBOARD
                        try
                        {
                            clipclient = new ClipMngrClient(((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString(), 14566, this);
                            
                        }
                        catch (Exception)
                        {
                            //nel caso vi siano problemi con la connessione alla clipboard avvisa l'utente ma permetti di proseguire
                            //con mouse e tastiera
                            this.ShowMessageAsync("","errore connessione clipboard, puoi provare a riconnetterti");
                        }
                        
                        //setta tutti i parametri della UI, avvia il bordo rosso e mette la finestra in trayIcon
                        tcpClientConnected.Set();
                        stato = "active";
                        this.Dispatcher.Invoke(() => { 
                            mtblk.Text = stato;
                            wRB.Show();
                            this.Visibility = System.Windows.Visibility.Hidden;
                            tb.Visibility = System.Windows.Visibility.Visible;
                        });

                        //System.Windows.MessageBox.Show("Client Autenticato");

                        break;
                    }
                    else //caso in cui l'autenticazione non sia andata a buon fine
                    {

                        Dispatcher.Invoke(() =>
                        {
                            this.ShowMessageAsync("Connessione rifiutata", "Connessione in entrata rifiutata a causa di credenziali errate");
                        }
                        );

                        //setta parametri per azzerare le connessioni e invia un KO al client
                        active = false;
                        rejected = true;
                        byte[] msg = System.Text.Encoding.ASCII.GetBytes("KO!");
                        stream.Write(msg, 0, msg.Length);
                        client.GetStream().Close();
                        client.Close();
                        listener.Stop();
                        client = null;
                        listener = null;
                        return;
                    }
                }

                //NetworkStream ns = new NetworkStream(client.Client);
                //ns.WriteTimeout = 1;
                //byte[] frisco=new byte[256];
                //ns.BeginWrite(frisco, 0, 255, new AsyncCallback(sendThings), ns);

                //se qui allora autenticazione riuscita e andata a buon fine
                try
                {
                    //TODO qui non viene gestita la object disposed exception
                    while ((i = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        // Translate data bytes to a ASCII string.
                        data = System.Text.Encoding.ASCII.GetString(buffer, 0, i);
                        try
                        {
                            foreach (Match item in R3.Matches(data)) 
                            {
                                //riceviamo le informazioni sullo stato della connessione col client (Attivo/pausa)    
                                //oppure height e width del client per normalizzare le coordinate del mouse
                            
                                if (item.Value == "{active}")
                                {
                                    active = true;
                                    stato = "attivo";
                                    this.Dispatcher.Invoke(() => {
                                        mtblk.Text = stato;
                                        wRB.Visibility = Visibility.Visible;
                                    });
                                }
                                else if (item.Value == "{pause}")
                                {
                                    active = false;
                                    stato = "in pausa";
                                    this.Dispatcher.Invoke(() => { 
                                        mtblk.Text = stato;
                                        wRB.Visibility = Visibility.Hidden;
                                    });
                                }
                                else
                                {
                                    var PC = item.Value.Substring(1, item.Value.Length - 2).Split(';');
                                    clientWidth = double.Parse(PC[0]);
                                    clientHeight = double.Parse(PC[1]);
                                }

                            }

                            foreach (Match item in R5.Matches(data))
                            {
                                //si elaborano le coordinate del mouse
                                var P = item.Value.Substring(1,item.Value.Length-2).Split(';');
                                myx = (int)((int.Parse(P[0]) / clientWidth) * thisWid);     //normalizzo le coordinate
                                myy = (int)((int.Parse(P[1]) / clientHeight) * thisHei);
                                
                                //write the log
                                //sw.WriteLine(myx.ToString() +","+ myy.ToString());

                                SetCursorPos(myx, myy);
                            }


                            foreach (Match item in R.Matches(data))
                            {
                                //ricevo pressione tasti mouse (destro, sinistro e scroll)

                                if (item.ToString() == "(SGiu)" && item.NextMatch().ToString() == "(SSu)")
                                {
                                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                                }
                                else if (item.ToString() == "(SGiu)")
                                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                                else if (item.ToString() == "(SSu)")
                                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                                else if (item.ToString() == "(DGiu)" && item.NextMatch().ToString() == "(DSu)")
                                {
                                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                                }
                                else if (item.ToString() == "(DGiu)")
                                {
                                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                                }
                                else if (item.ToString() == "(DSu)")
                                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                                else if (item.ToString() == "(Wu)")
                                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, 120, 0);
                                else if (item.ToString() == "(Wd)")
                                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -120, 0);
                                else if (item.ToString() == "(Wtd)")
                                    mouse_event(WM_MBUTTONDOWN, 0, 0, 0, 0);
                                else if (item.ToString() == "(Wtu)")
                                    mouse_event(WM_MBUTTONUP, 0, 0, 0, 0);
                                
                            }

                            foreach (Match item in R2.Matches(data))
                            //in questi due foreach viene gestita la pressione e il rilascio dei tasti della tastiera
                            {

         
                                var P = item.Value.Substring(1, item.Value.Length - 2);
                                Key k;
                                bool val = Enum.TryParse(P, out k);
                                var vk = KeyInterop.VirtualKeyFromKey(k);   //KeyInterop fornisce i metodi per convertire i codici della tastiera
                                if (vk != 0)
                                {
                                    keybd_event((byte)vk, /*(byte)MapVirtualKey((uint)vk,0)*/0, 0, 0);             //da Win32 a WPF e viceversa
                                }

                                //var P = item.Value.Substring(1, item.Value.Length - 2);
                                //uint k;
                                //bool val = uint.TryParse(P, out k);
                                //keybd_event((byte)MapVirtualKeyEx(k, 1, LoadKeyboardLayout((GetKeyboardLayout(0).ToInt32() & 0xFFFF).ToString(), 0x00000100)), 0, 0, 0);
                                //keybd_event((byte)MapVirtualKey(k, 1), 0, 0, 0);
                                
                            }


                            foreach (Match item in R4.Matches(data))
                            {

                                var P = item.Value.Substring(1, item.Value.Length - 2);
                                Key k;
                                bool val = Enum.TryParse(P, out k);
                                var vk = KeyInterop.VirtualKeyFromKey(k);
                                if (vk != 0)
                                {
                                    keybd_event((byte)vk, /*(byte)MapVirtualKey((uint)vk, 0)*/ 0, KEYEVENTF_KEYUP, 0);
                                }

                                //var P = item.Value.Substring(1, item.Value.Length - 2);
                                //uint k;
                                //bool val = uint.TryParse(P, out k);
                                //keybd_event((byte)(int)MapVirtualKeyEx((uint)k, 1, LoadKeyboardLayout((GetKeyboardLayout(0).ToInt32() & 0xFFFF).ToString(), 0x00000100)), 0, KEYEVENTF_KEYUP, 0);
                                
                            }

                        }
                        catch (Exception f)
                        {
                            System.Windows.MessageBox.Show("La connessione è stata chiusa a causa di un errore in lettura");
                            break;
                        }

                    }

                    //QUI il client ha chiuso, quindi interrompo anche la clipboard
                    bool c=false;
                    if(clipclient!=null) clipclient.closeMngr(out c);
                    if(client!=null){
                        clipclient = null;
                        client.GetStream().Close();
                        client.Close();
                        client = null;
                    }
                    Dispatcher.Invoke(() =>
                    {
                        this.ShowMessageAsync("Connessione chiusa", "Connessione chiusa correttamente");
                    }
                    );
                }
                //catch (NullReferenceException nre)
                //{
                //    //non può più succedere, ignora
                //    // System.Windows.MessageBox.Show(nre.ToString());
                //}
                catch (Exception sioe)
                { //qui può esservi un eccezione a causa della chiusura della connessione da parte del client o dell'utente sul server (tasto Interrompi o uscita)

                    //se il flag è true la connessione è stata chiusa volontariamente dal tasto interrompi
                    if (flag)
                    {
                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                this.ShowMessageAsync("", "connessione chiusa");
                            });
                        }
                        catch (OperationCanceledException) { };
                        flag = false;
                        
                    }
                    else
                    {//se il flag è false la connessione non è stata chiusa volontariamente, quindi si procede a chiuderla in maniera safe.
                        Dispatcher.Invoke(() =>
                        {
                            this.ShowMessageAsync("Attenzione", "connessione chiusa");
                        });

                        //chiusura socket periferiche e clipboard
                        bool c;
                        if(clipclient!=null) clipclient.closeMngr(out c);
                        if(client!=null){
                            clipclient = null;//N
                            if(client.Connected){
                                client.GetStream().Close();
                                client.Close();
                            }
                        }
                        client = null;
                    }
                }
                finally
                {
                    //operazioni da fare sempre quando è stata chiusa la connessione
                    if(listener!= null) listener.Stop();
                    if(server!=null) server.Stop();
                    listener = null;
                    server = null;
                    rejected = true;
                    this.Dispatcher.Invoke(() =>
                    {
                        mtblk.Text = "disconnesso";
                        wRB.Visibility = Visibility.Hidden;
                    });
                }
            }
        }


        //per togliere la finestra dalla tryIcon e portarla di nuovo in primo piano
        private void MenuItem_Foreground(object sender, RoutedEventArgs e)
        {
            tb.Visibility = System.Windows.Visibility.Hidden;
            this.Visibility = System.Windows.Visibility.Visible;
        }

        //per mettere la finestra in tryIcon
        private void MetroWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                this.Visibility = System.Windows.Visibility.Hidden;
                tb.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void Window_Closed(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //sw.Close();
            //flog.Close();

            //se non vi sono connessioni neanche in ascolto chiude la finestra rettangolo rosso e la tryIcon
            if (server == null)
            {
                wRB.Close();
                tb.Visibility = System.Windows.Visibility.Hidden;
                tb.Dispose();
                source.RemoveHook(WndProc);
                e.Cancel = false;
            }
            else
            {
                
                if (client != null) // vi e una connessione attiva
                {
                    if (System.Windows.MessageBox.Show("Stai tentando di interrompere il programma mentre un client è attivo, Procedere?", "Client connesso", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Stop) == System.Windows.MessageBoxResult.Yes)
                    {
                        bool closed=true;
                        if(clipclient!=null) clipclient.closeMngr(out closed);
                        if (closed)
                        {
                            clipclient = null;
                            flag = true;
                            active = false;
                            client.GetStream().Close();
                            client.Close();
                            client = null;
                            //
                            source.RemoveHook(WndProc);
                            e.Cancel = false;
                            wRB.Close();
                            tb.Visibility = System.Windows.Visibility.Hidden;
                            tb.Dispose();
                            //
                            if (server != null) server.Stop();
                            server = null;
                        }
                        else
                        {
                            e.Cancel = true;
                        }
                    }
                    else
                    {
                        e.Cancel = true;
                    }

                }
                else //qui se il server è in ascolto
                {
                    server.Stop();
                    server = null;
                    wRB.Close();
                    tb.Visibility = System.Windows.Visibility.Hidden;
                    tb.Dispose();
                    source.RemoveHook(WndProc);
                    e.Cancel = false;
                }

            }
            
        }

        private void Button_Click_2(object sender, RoutedEventArgs e) //bottone per aggiornare gli indirizzi
        {
            listenintf.Items.Clear();
            IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

            foreach (var it in localIPs)
            {
                if (it.AddressFamily == AddressFamily.InterNetwork)
                {
                    listenintf.Items.Add(it.ToString());
                }
            }

            listenintf.SelectedIndex = listenintf.Items.Count > 0 ? 0 : -1;
        }

        private void Button_Click_3(object sender, RoutedEventArgs e) //bottone per riconnettere clipboard
        {
            if (clipclient != null)
            {
                bool f=false;
                clipclient.closeMngr(out f);
                if (client != null && f)
                {
                    clipclient = new ClipMngrClient(((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString(), 14566, this);
                    
                }
            }
        }

        /*
        private void sendThings(IAsyncResult a)
        {
                NetworkStream mystream = ((NetworkStream)a.AsyncState);
                //mystream.WriteTimeout = 100;
            try
            {
                    mystream.EndWrite(a);
            }
            catch
            {
                System.Windows.MessageBox.Show("Connessione caduta");
                return;
            }
            
            mystream.BeginWrite(new byte[256], 0, 255, new AsyncCallback(sendThings), mystream);
        }
        */
    }
    
}
