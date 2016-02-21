using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace ClientSide
{
    /// <summary>
    /// Logica di interazione per MainWindow.xaml
    /// </summary>
  
        public partial class MainWindow
        {

            //coordinate dello schermo(usate per la normalizzazione dei movimenti del mouse)
            private String scrWidth = System.Windows.SystemParameters.PrimaryScreenWidth.ToString();
            private String scrHeight = System.Windows.SystemParameters.PrimaryScreenHeight.ToString();
            private bool coord; //questo flag mi serve per mandare le coordinate al server una volta sola

            public int ind; //indice usato nella lista dei server

            private bool capturing; //flag che mi indica se il server è attivo, in pausa o disconnesso
            private int currActive; //indice del server attualmente connesso
            public  ObservableCollection<RemoteServer> res = new ObservableCollection<RemoteServer>();

            //CLIPBOARD SERVER
            private TcpListener ClipboardServer;
            private Thread ascoltando;
            private int selectedintf;   //indice usato sullo switch dei server
            private string ascattuale; //interfaccia di ascolto clipboard attualmente connessa
            public RoutedCommand ReturnControl = new RoutedCommand();

            [DllImport("user32.dll")]
            static extern uint MapVirtualKey(uint uCode, uint uMapType);

            [DllImport("user32.dll")]
            static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

            [DllImport("user32.dll")]
            static extern IntPtr GetKeyboardLayout(uint idThread);

            [DllImport("user32.dll")]
            static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

            //all'avvio del programma inserisco le istruzioni nella textblock e metto in ascolto il thread della clipboard
            public MainWindow()
            {
                WindowState = WindowState.Maximized;
                //WindowStyle = WindowStyle.None;
                capturing = false;
                currActive = -1;
                coord = false;
               
                InitializeComponent();

                lVconn.DataContext = res;
                selectedintf = 0;
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (var it in localIPs)
                {
                    if (it.AddressFamily == AddressFamily.InterNetwork)
                    {
                        toto.Items.Add(it.ToString());
                        
                    }
                }
                //toto.Items.Add("127.0.0.1");
                ascattuale=toto.Items.Count > 0 ? toto.Items.GetItemAt(0).ToString() : "127.0.0.1";
                toto.SelectedIndex = toto.Items.Count > 0 ? 0 : -1;
                ClipboardServer = new TcpListener(IPAddress.Parse(ascattuale), 14566);
                ClipboardServer.Start();
                ThreadStart ts = new ThreadStart(Ascolta);
                ascoltando=new Thread(ts);
                ascoltando.Start();

                TBlockInfo.Visibility = System.Windows.Visibility.Hidden;
                TBlockInfo.Inlines.Add(new Run("- Ctrl + b") { FontWeight = FontWeights.Bold });
                TBlockInfo.Inlines.Add(": mettere in pausa il server e riottenere il controllo della propria macchina." + Environment.NewLine + Environment.NewLine);
                TBlockInfo.Inlines.Add(new Run("- Ctrl + alt + s") { FontWeight = FontWeights.Bold });
                TBlockInfo.Inlines.Add(": per cambiare il server da controllare." + Environment.NewLine + Environment.NewLine);
                TBlockInfo.Inlines.Add(new Run("- Ctrl + alt + v") { FontWeight = FontWeights.Bold });
                TBlockInfo.Inlines.Add(": per incollare un dato nella clipboard del server." + Environment.NewLine + Environment.NewLine);
                TBlockInfo.Inlines.Add(new Run("- Doppio click su un server") { FontWeight = FontWeights.Bold });
                TBlockInfo.Inlines.Add(": attivare il server." + Environment.NewLine + Environment.NewLine);

            }

            //questa procedura è adibita all'avvio della connessione con il server
            private void Button_Click(object sender, RoutedEventArgs e)
            {
                var p = -1;
                var isnew = true;
                IPAddress ipad;
                String pas;
                Int32 pp;
                bool verIP = IPAddress.TryParse(mytb.Text.ToString(), out ipad);
                bool verPs = mypw.Password.ToString().Length != 0 || mypw.Password.ToString() != "";
                bool verPor = int.TryParse(defa.Text.ToString(), out pp);

                if (verIP && verPs && verPor)
                {
                    pas = mypw.Password.ToString();
                }
                else
                {
                    String ls = "";     //qui viene fatta la validazione delle credenziali
                    int cnt = 0;
                    if (!verIP)
                    {
                        cnt++;
                        ls = ls + " IP \n";
                    } if (!verPs)
                    {
                        cnt++;
                        ls = ls + " Password \n";
                    } if (!verPor)
                    {
                        cnt++;
                        ls = ls + " Porta \n";
                    }
                    if (cnt > 1)
                    {
                        this.ShowMessageAsync("", "i segeuent campi sono errati:\n" + ls);
                    }
                    else
                    {
                        this.ShowMessageAsync("", "il campo" + ls + "è errato");
                    }
                    return;
                }

                foreach (var m in res)
                {

                    if (m.ip.Equals(ipad))
                    {
                        if (m.status == "error")
                        {
                            m.so.Shutdown(SocketShutdown.Both);
                            m.so.Close();
                            p = res.IndexOf(m);
                            isnew = false;
                        }
                        else if (m.status == "disconnected")
                        {
                            p = res.IndexOf(m);
                            isnew = false;
                        }
                        else
                        {
                            this.ShowMessageAsync("", "Questo server è già conesso");
                            return;
                        }

                    }
                }
                if (isnew)
                {

                    res.Add(new RemoteServer(mytb.Text.ToString(), Int32.Parse(defa.Text.ToString())));

                    if (res.ElementAt(res.Count() - 1).StartAuth(mypw.Password.ToString()) == false)
                    {
                        res.RemoveAt(res.Count() - 1);
                    }
                    else
                    {             
                        capturing = true;
                        currActive = res.Count() - 1;
                        coord = false;
                    };
                }
                else
                {
                    //riprova a connettersi
                    if (res.ElementAt(p).StartAuth(mypw.Password.ToString()) == false)
                    {
                        res.RemoveAt(p);
                    }
                    else
                    {
                        capturing = true;
                        currActive = res.Count() - 1;
                    };
                }
            }

            //queste 8 procedure si occupa di inviare al server i movimenti,
            //la pressione, il rilascio dei pulsanti del mouse e lo scroll della rotellina
            protected override void OnPreviewMouseMove(MouseEventArgs e)
            {
                if (capturing && currActive >= 0)
                {
                    if (res.ElementAt(currActive).status == "disconnected")
                    {
                        capturing = false;
                    }
                    e.Handled = true;
                    if (coord == false)
                    {
                        res[currActive].Send("{"+scrWidth+";"+scrHeight+"}");
                        coord = true;
                    }
                    
                    System.Windows.Point p = e.GetPosition(this);
                    String poi = "(" + p.ToString() + ")";
                    res.ElementAt(currActive).Send(poi);

                }
            }

            protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
            {
                if (capturing)
                {
                    e.Handled = true;
                    res[currActive].Send("(SGiu)");
                }
            }

            private void PrMouseLeftBtUp(object sender, MouseButtonEventArgs e)
            {
                if (capturing)
                {
                    e.Handled = true;
                    res[currActive].Send("(SSu)");
                }
                
            }
            
            private void PrMouseRightBtDown(object sender, MouseButtonEventArgs e)
            {
                if (capturing)
                {
                    e.Handled = true;
                    res[currActive].Send("(DGiu)");
                }
            }

            private void PrMouseRightBtUp(object sender, MouseButtonEventArgs e)
            {
                if (capturing)
                {
                    e.Handled = true;
                    res[currActive].Send("(DSu)");
                }
            }

            private void PrMouseWheel(object sender, MouseWheelEventArgs e)
            {
                int delta = (int)e.Delta;
                if (capturing)
                {
                    e.Handled = true;
                    if (delta > 0)
                        res[currActive].Send("(Wd)");
                    else
                        res[currActive].Send("(Wu)");
                    
                }
            }

            private void PrMouseDown(object sender, MouseButtonEventArgs e)
            {
                if (capturing && e.MiddleButton == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                    res[currActive].Send("(Wtd)");
                }
            }
            private void PrMouseUp(object sender, MouseButtonEventArgs e)
            {
                if (capturing && e.MiddleButton == MouseButtonState.Released)
                {
                    e.Handled = true;
                    res[currActive].Send("(Wtu)");
                }
            }

            //queste due funzioni servono ad inviare la pressione ed il rilascio dei tasti della tastiera
            private void OnPreKeyDown(object sender, KeyEventArgs e)
            {
                //questi 3 flag mi dicono se una di queste 3 combinazioni è stata premuta: Ctrl + b, Ctrl + alt + s o Ctrl + alt + v.
                bool b = Keyboard.IsKeyDown(Key.B) && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl));
                bool switcher = Keyboard.IsKeyDown(Key.S) && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt));
                bool RemoteSend = Keyboard.IsKeyDown(Key.V) && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt));

                if (b)
                {
                    if (capturing)  //se è stata premuta la combinazione Ctrl + b, allora metto il server in pausa
                    {
                        res[currActive].Send("<" + Key.LeftCtrl.ToString() + ">");
                        res.ElementAt(currActive).Send("{pause}");
                        res[currActive].setStatus("pause");
                        capturing = false;
                        currActive = -1;
                        coord = false;

                    }
                }
                if (switcher)   //se è stata premuta la combinazione Ctrl + alt + s, allora cambio server
                {               //se ce n'è uno solo nella lista, lo metto in active o in pausa
                    int xActive = currActive;

                    if (currActive >= 0)
                    {
                        res[currActive].Send("<" + Key.LeftCtrl.ToString() + ">");
                        res[currActive].Send("<" + Key.LeftAlt.ToString() + ">");
                        res.ElementAt(currActive).Send("{pause}");
                        res[currActive].setStatus("pause");
                        if (++xActive > res.Count - 1)
                        {
                            currActive = -1;
                            capturing = false;
                            coord = false;
                        }
                        else
                        {
                            currActive = xActive;
                            coord = false;
                            capturing = true;
                            res.ElementAt(currActive).setStatus("active");
                            res.ElementAt(currActive).Send("{active}");
                        }
                    }
                    else
                    {
                        if (++xActive > res.Count - 1)
                        {
                            currActive = -1;
                            capturing = false;
                            coord = false;
                        }
                        else
                        {
                            currActive = xActive;
                            coord = false;
                            capturing = true;
                            res.ElementAt(currActive).setStatus("active");
                            res.ElementAt(currActive).Send("{active}");
                        }
                    }
                }
                if (capturing)  //se è stata premuta la combinazione Ctrl + alt + v, allora devo inviare il contenuto della
                {               //clipboard al server attualmente attivo
                    if (RemoteSend)
                    {
                        e.Handled = true;
                        if (!(currActive < 0))
                        {
                            res[currActive].Send("<" + Key.LeftCtrl.ToString() + ">");
                            res[currActive].Send("<" + Key.LeftAlt.ToString() + ">");
                            res.ElementAt(currActive).SendClipOb();
                        }
                    }
                    else
                    {   //se non è stata premuta nessuna combinazione speciale di tasti, mi limito ad inviare i tasti digitati
                        if (!switcher)
                        {
                            e.Handled = true;
                            if (e.Key == Key.System)
                            {
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, LoadKeyboardLayout((GetKeyboardLayout(0).ToInt32() & 0xFFFF).ToString(), 0x00000100)) + "]";
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, (IntPtr) (GetKeyboardLayout(0).ToInt32() & 0xFFFF)).ToString() + "]";
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, GetKeyboardLayout(0)).ToString() + "]";
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, (IntPtr)Thread.CurrentThread.CurrentCulture.KeyboardLayoutId).ToString() + "]";
                                //String poi = "[" + MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0).ToString() + "]";
                                String poi = "[" + e.SystemKey.ToString() + "]";
                                res[currActive].Send(poi);
                                //System.Windows.MessageBox.Show("Il tasto è : " + MapVirtualKey(KeyInterop.VirtualKeyFromKey(e.SystemKey), 0).ToString());
                            }
                            else
                            {
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, LoadKeyboardLayout((GetKeyboardLayout(0).ToInt32() & 0xFFFF).ToString(), 0x00000100)) + "]";
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, (IntPtr)(GetKeyboardLayout(0).ToInt32() & 0xFFFF)).ToString() + "]";
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, GetKeyboardLayout(0)).ToString() + "]";
                                //String poi = "[" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, (IntPtr)Thread.CurrentThread.CurrentCulture.KeyboardLayoutId).ToString() + "]";
                                //String poi = "[" + MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0).ToString() + "]";
                                String poi = "[" + e.Key.ToString() + "]";
                                res[currActive].Send(poi);
                                //System.Windows.MessageBox.Show("Il tasto è : " + MapVirtualKey(KeyInterop.VirtualKeyFromKey(e.Key), 0).ToString());
                            }

                        }
                    }
                }


            }

            private void OnPreKeyUp(object sender, KeyEventArgs e)
            {
                if (capturing)
                {
                    e.Handled = true;
                    if (e.Key == Key.System)
                    {
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, LoadKeyboardLayout((GetKeyboardLayout(0).ToInt32() & 0xFFFF).ToString(), 0x00000100)) + ">";
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, (IntPtr)(GetKeyboardLayout(0).ToInt32() & 0xFFFF)).ToString() + ">";
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, GetKeyboardLayout(0)).ToString() + ">";
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0, (IntPtr)Thread.CurrentThread.CurrentCulture.KeyboardLayoutId).ToString() + ">";
                        //String poi = "<" + MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(e.SystemKey), 0).ToString() + ">";
                        String poi = "<" + e.SystemKey.ToString() + ">";
                        res[currActive].Send(poi);
                    }
                    else
                    {
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, LoadKeyboardLayout((GetKeyboardLayout(0).ToInt32() & 0xFFFF).ToString(), 0x00000100)) + ">";
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, (IntPtr)(GetKeyboardLayout(0).ToInt32() & 0xFFFF)).ToString() + ">";
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, GetKeyboardLayout(0)).ToString() + ">";
                        //String poi = "<" + MapVirtualKeyEx((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0, (IntPtr)Thread.CurrentThread.CurrentCulture.KeyboardLayoutId).ToString() + ">";
                        //String poi = "<" + MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(e.Key), 0).ToString() + ">";
                        String poi = "<" + e.Key.ToString() + ">";
                        res[currActive].Send(poi);
                    }
                }
            }

            //questa funzione è collegata al click del bottone "chiudi tutti" che disconnette tutti i server
            //attualmente connessi
            private void Button_Click_1(object sender, RoutedEventArgs e)
            {
                if (res.Count > 0)
                {
                    var k = res.Count;

                    for (int j = 0; j < k; j++)
                    {
                        res.ElementAt(j).DisConnect();
                    }

                    for (int j = 0; j < k; j++)
                    {
                        res.RemoveAt(0);
                    }
                }
                else
                {
                    this.ShowMessageAsync("", "Non sono attualmente connessi servers");
                }
            }

            //disconnette il server selezionato (associata al bottone "Disconnetti")
            private void Button_Click_2(object sender, RoutedEventArgs e)
            {
                ind = lVconn.SelectedIndex;
                if (ind >= 0)
                {
                    res.ElementAt(ind).DisConnect();
                    if(res.ElementAt(ind).status=="disconnected"){
                        res.RemoveAt(ind);
                    }
                }
                else
                {
                    this.ShowMessageAsync("", "Non hai selezionato alcun server");
                }
            }

            //assume il controllo sul server selezionato (associata al bottone "Passa a")
            private void Button_Click_4(object sender, RoutedEventArgs e)
            {
                ind = lVconn.SelectedIndex;

                if (ind >= 0)
                {
                    currActive = ind;
                    coord = false;
                    capturing = true;
                    res.ElementAt(currActive).status = "active";
                    res.ElementAt(currActive).Send("{active}");
                }
                else
                {
                    this.ShowMessageAsync("", "Non hai selezionato alcun server");
                }
            }

            //collegato all'evento "selezione di un altro server" (la procedura aggiorna l'indice nella lista dei server)
            private void lVconn_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                
                ind = lVconn.SelectedIndex;
            }

            //questa procedura fa sì che il thread si metta in ascolto sull'interfaccia della clipboard
            public void Ascolta()
            {
                
                while (true)
                {
                    try
                    {

                    TcpClient client = ClipboardServer.AcceptTcpClient();
                    bool wasrecognized = false;
                    //associa 
                    foreach (var x in res){
                        if (x.ip.Equals(((IPEndPoint)client.Client.RemoteEndPoint).Address))
                        {
                            x.cmc = new ClipMngrClient(client, this);
                            wasrecognized = true;
                        }
                    }

                    if (wasrecognized==false)
                    {
                        client.GetStream().Close();
                        client.Close();
                    }
                    }
                    catch (Exception e)
                    {
                        break;
                    }
                }

            }

            //operazioni da fare alla chiusura del programma
            private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
            {
                foreach (var x in res)
                {
                    if (x.DisConnect() == false)
                    {
                        e.Cancel = true;    //se ho scelto intenzionalmente di annullare la chiusura, allora lo comunico al programma
                        return;
                    }
                }
                res = null;
                ClipboardServer.Stop();
            }

            //collegato all'evento "selezione di un altra interfaccia clipboard"
            //(la procedura aggiorna l'indice nella lista delle interfacce clipboard)
            private void toto_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                selectedintf = toto.SelectedIndex;
            }

            //procedura collegata al bottone cambia (cambio l'interfaccia di ascolto della clipboard)
            private void Button_Click_3(object sender, RoutedEventArgs e)
            {
                    ClipboardServer.Stop(); 
                    ascoltando.Join();
                    ClipboardServer = new TcpListener(IPAddress.Parse(toto.Items.GetItemAt(selectedintf).ToString()), 14566);
                    ClipboardServer.Start();
                    ThreadStart ts = new ThreadStart(Ascolta);
                    ascoltando = new Thread(ts);
                    ascoltando.Start();
            }

            //procedura collegata al doppio click sul server (cambia lo stato da pausa ad active)
            private void lV_dbclic(object sender, MouseButtonEventArgs e)
            {
                Button_Click_4(sender, e);
            }

            //procedura che consente di aggiornare dinamicamente le interfacce della clipboard
            private void Button_Click_5(object sender, RoutedEventArgs e)
            {
                toto.Items.Clear();
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (var it in localIPs)
                {
                    if (it.AddressFamily == AddressFamily.InterNetwork)
                    {
                        toto.Items.Add(it.ToString());

                    }
                }
                ascattuale = toto.Items.Count > 0 ? toto.Items.GetItemAt(0).ToString() : "127.0.0.1";
                toto.SelectedIndex = toto.Items.Count > 0 ? 0 : -1;
            }

            //se per sbaglio è stata ridimensionata la finestra, questa procedura consente di riportarla a schermo intero
            private void Button_Click_6(object sender, RoutedEventArgs e)
            {
                WindowState = WindowState.Maximized;
            }

            //bottone che mostra/nasconde le istruzioni
            private void Button_Click_Info(object sender, RoutedEventArgs e)
            {
                if (TBlockInfo.IsVisible)
                    TBlockInfo.Visibility = System.Windows.Visibility.Hidden;
                else
                    TBlockInfo.Visibility = System.Windows.Visibility.Visible;
            }
        }

    }

