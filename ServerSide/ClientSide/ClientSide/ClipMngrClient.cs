using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

//E' POSSIBILE TROVARE LA DOCUMENTAZIONE NELLA STESSA CLASSE DI SERVERSIDE

namespace ClientSide
{

    public class ClipMngrClient 
    {
        //  GLOBAL VARIABLES FOR STOPPING AND MANAGING THREADS
        // one boolean, two monitors

        private  volatile bool work_;
        private  Object monClip = new Object();
        private  Object monSend = new Object();
        private  Object SendStop = new Object();

        //quella usata sempre per ottenere il MIME type
        [DllImport(@"urlmon.dll", CharSet = CharSet.Auto)]
        private extern static uint FindMimeFromData(
            uint pBC,
            [MarshalAs(UnmanagedType.LPStr)] string pwzUrl,
            [MarshalAs(UnmanagedType.LPArray)] byte[] pBuffer,
            uint cbSize,
            [MarshalAs(UnmanagedType.LPStr)] string pwzMimeProposed,
            uint dwMimeFlags,
            out uint ppwzMimeOut,
            uint dwReserverd
        );

        //percorsi utilizzati
        private string pathS;
        private string pathD;
        ZipOutputStream zipStream;

        enum FileType
        {
            zip, img, audio, text
        };

        private FileType ft;
        private int filen;

        //risorse per invio
        private TcpClient client;
        private byte[] sendbuff;
        private ManualResetEvent fileclosed;
        private ManualResetEvent filesendingclosed;

        //risorse per la ricezione
        private byte[] recbuff;
        private Thread treceiver;

        //risorse per la socket
        private string hostname;
        private int port;
        public Window window;
        private bool isclient;
        private volatile bool icos;

        public ClipMngrClient(string hname, int pr, Window w)
        {
            try
            {
                icos = false;
                work_ = true;
                recbuff = new byte[1024];
                sendbuff = new byte[1024];
                fileclosed = new ManualResetEvent(false);
                filesendingclosed = new ManualResetEvent(false);
                isclient = true;
                port = pr;
                hostname = hname;
                zipStream = null;
                client = new TcpClient(hname, pr);


                this.window = w;

                //gestisci i path temporanei
                pathS = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
                while (Directory.Exists(pathS))
                {
                    //per essere sicuri al 100% che non ci sia già una temp con quel nome
                    pathS = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
                }

                System.IO.Directory.CreateDirectory(pathS);
                filen = 0;
                pathD = System.IO.Path.Combine(pathS, "extracted"); //   directory path C:\user\...\tempXXX\extracted\
                System.IO.Directory.CreateDirectory(pathD);

                //AVVIA THREAD DI RICEZIONE
                //ThreadStart ts = new ThreadStart(startReceive);
                treceiver = new Thread(startReceive);
                treceiver.SetApartmentState(ApartmentState.STA);
                treceiver.Start();

            }
            catch (SocketException se)
            {
                window.Dispatcher.BeginInvoke((Action)(() => { MessageBox.Show("ATTENZIONE: Clipboard non condivisa - Errore di connessione"); }));
                work_ = false;
            }
            catch (Exception e)
            {
                window.Dispatcher.BeginInvoke((Action)(() => { MessageBox.Show("ATTENZIONE: Clipboard non condivisa"); }));
                work_ = false;
            }

        }

        public ClipMngrClient(TcpClient c, Window window)
        {
            this.window = window;
            
            try
            {
                icos = false;
                isclient = false;
                work_ = true;
                recbuff = new byte[1024];
                sendbuff = new byte[1024];
                fileclosed = new ManualResetEvent(false);
                filesendingclosed = new ManualResetEvent(false);

                client = c;

                //gestisci i path temporanei
                pathS = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
                while (Directory.Exists(pathS))
                {
                    //per essere sicuri al 100% che non ci sia già una temp con quel nome
                    pathS = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
                }
                System.IO.Directory.CreateDirectory(pathS); //pathS è la mia "root" del programma
                filen = 0;
                pathD = System.IO.Path.Combine(pathS, "extracted"); //   directory path C:\user\...\tempXXX\extracted\
                System.IO.Directory.CreateDirectory(pathD);


                //AVVIA THREAD DI RICEZIONE
                //ThreadStart ts = new ThreadStart(startReceive);
                treceiver = new Thread(startReceive);
                treceiver.SetApartmentState(ApartmentState.STA);
                //treceiver.IsBackground = false;
                treceiver.Start();

            }
            catch (SocketException se)
            {
                window.Dispatcher.BeginInvoke((Action)(() => { MessageBox.Show("ATTENZIONE: Clipboard non condivisa - Errore di connessione"); }));
                work_ = false;
            }
            catch (Exception e)
            {
                window.Dispatcher.BeginInvoke((Action)(() => { MessageBox.Show("ATTENZIONE: Clipboard non condivisa"); }));
                work_ = false;
            }

        }

        public void startReceive()
        {

            while (work_)
            {
                string pathSrc2 = System.IO.Path.Combine(pathS, ("archivio_" + filen.ToString() + ".zip"));
                MemoryStream mS = new MemoryStream();
                FileStream fS = new FileStream(pathSrc2, FileMode.OpenOrCreate);
                bool flag = true;
                string mime = "";
                bool iszip = false;
                UInt64 recved = 0;
                UInt64 lf = 0;

                try
                {
                    //operazioni di rete
                    NetworkStream nsr = client.GetStream();
                    byte[] bb = new byte[8];

                    int i = 0;
                    int letto = nsr.Read(bb, 0, bb.Length);
                    lf = BitConverter.ToUInt64(bb, 0);
                    if (letto == 0)
                    {
                        fS.Close();
                        fS.Dispose();
                        fileclosed.Set();
                        break;
                    }

                    while ((i = nsr.Read(recbuff, 0, recbuff.Length)) > 0)
                    {
                        recved += (UInt64)i;

                        if (flag)
                        {
                            uint mimeType;
                            FindMimeFromData(0, null, recbuff, (uint)recbuff.Length, null, 0, out mimeType, 0);
                            var mimePointer = new IntPtr(mimeType);
                            mime = Marshal.PtrToStringAuto(mimePointer);
                            Marshal.FreeCoTaskMem(mimePointer);

                            flag = !flag;

                            if (mime.Contains("application/x-zip-compressed"))
                            {
                                iszip = true;
                                ft = FileType.zip;
                            }
                            else if (mime.Contains("image"))
                            {
                                ft = FileType.img;
                            }
                            else if (mime.Contains("wav"))
                            {
                                ft = FileType.audio;
                            }
                            else
                            {
                                ft = FileType.text;
                            }

                        }

                        //se è zip scrivi sul filestream, altrimenti sul memorystream
                        if (iszip) { fS.Write(recbuff, 0, i); }
                        else mS.Write(recbuff, 0, i);

                        //quando finisci di leggere il file interrompi il ciclo
                        if (recved >= lf) break;
                    }

                }
                catch (Exception)
                {
                    fS.Close();
                    fS.Dispose();
                    mS.Close();
                    mS.Dispose();

                    fileclosed.Set();
                    break;
                }
                finally
                {
                    filen++;
                }


                if (recved == 0 || recved < lf)
                {// se sono qui vuol dire che in ricezione non ho avuto niente oppure non ho ricevuto il file completo
                    fS.Close();
                    fS.Dispose();
                    fileclosed.Set();
                    break;
                }

                fS.Close();
                fS.Dispose();

                //operazioni sulla clipboard post-ricezione
                if (ft == FileType.img && work_)
                {
                    mS.Seek(0, SeekOrigin.Begin);
                    var bit = new BitmapImage();
                    bit.BeginInit();
                    bit.StreamSource = mS;
                    bit.CacheOption = BitmapCacheOption.OnLoad;
                    bit.EndInit();
                    bit.Freeze();
                    BitmapSource bitsr = bit as BitmapSource;

                    DataObject do1 = new DataObject();
                    do1.SetImage(bitsr);
                    Clipboard.SetDataObject(do1);
                    window.Dispatcher.Invoke(() =>
                    {
                        Monitor.Enter(monClip);
                        Clipboard.Clear();
                        Clipboard.SetDataObject(do1);
                        Monitor.Exit(monClip);
                    });

                    mS.Dispose();

                }
                if (ft == FileType.zip && work_)
                {
                    String tempPath = System.IO.Path.Combine(pathD, System.IO.Path.GetRandomFileName()); // C:\user..\temp\pathS\extract\[NewRandomFileName]
                    if (Directory.Exists(tempPath))
                    {
                        Directory.Delete(tempPath);
                    }

                    Directory.CreateDirectory(tempPath);
                    try
                    {
                     System.IO.Compression.ZipFile.ExtractToDirectory(pathSrc2, tempPath);
                    }
                    catch (PathTooLongException)
                    {
                        MessageBox.Show("Path troppo lungo - Impossibile incollare il/i file/files");
                        continue;
                    }

                    File.Delete(pathSrc2);  //cancello l'archivio zip, tanto nn mi serve +
                    StringCollection stC = new StringCollection();
                    stC.AddRange(System.IO.Directory.GetDirectories(tempPath));    //metto in stC tutto ciò che sta in pathD (dato che ho eliminato archivio_n.zip ora in pathD, 
                    stC.AddRange(System.IO.Directory.GetFiles(tempPath));          //cioè in ClipMngTemp, ci saranno solo i files estratti)


                    window.Dispatcher.Invoke(() =>
                    {
                        Clipboard.Clear();
                        DataObject do1 = new DataObject();
                        //do1.SetText(tempPath);
                        do1.SetFileDropList(stC);
                        Clipboard.SetDataObject(do1);
                    });


                }
                if (ft == FileType.text && work_)
                {
                    mS.Seek(0, SeekOrigin.Begin);
                    StreamReader reader = new StreamReader(mS);
                    string testo = reader.ReadToEnd();
                    DataObject do1 = new DataObject();
                    do1.SetText(testo);

                    window.Dispatcher.Invoke(() =>
                    {
                        Monitor.Enter(monClip);
                        Clipboard.Clear();
                        Clipboard.SetDataObject(do1);
                        Monitor.Exit(monClip);
                    });

                    mS.Dispose();
                }

            }

            if (work_)
            {//se work non è stato settato a false significa che ha chiuso l'altro lato!
                //altrimenti sto chiudendo di qui e quindi non mi serve cancellare la directory
                //Directory.Delete(pathD);
            }
            else
            {
                fileclosed.Set();
            }
            icos = true;
        }

        public void SendClipObject()
        {

            if (Clipboard.ContainsText())
            {
                Thread sf = new Thread(new ThreadStart(SendText));
                sf.SetApartmentState(ApartmentState.STA);
                sf.Start();

            }
            else if (Clipboard.ContainsImage())
            {
                Thread sf = new Thread(new ThreadStart(SendImg));
                sf.SetApartmentState(ApartmentState.STA);
                sf.Start();
            }
            else if (Clipboard.ContainsFileDropList())
            {
                Thread sf = new Thread(new ThreadStart(SendFiles));
                sf.SetApartmentState(ApartmentState.STA);
                sf.Start();
            }

        }

        public void SendFiles()
        {
            FileInfo info;
            long clipSize = 0;
            if (Monitor.TryEnter(monSend))
            {
                ///non bloccante di per se poichè fa le tryEnter
                ///se vi è già il lock sul mutex monSend
                try
                {
                    NetworkStream ns = client.GetStream();

                    Monitor.Enter(monClip);
                    StringCollection scl = Clipboard.GetFileDropList();
                    Monitor.Exit(monClip);

                    foreach (var stringElement in scl)
                    {
                        if ((File.GetAttributes(stringElement) & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            string[] dirFiles = Directory.GetFiles(stringElement, "*.*", SearchOption.AllDirectories);
                            foreach (var name in dirFiles)
                            {
                                info = new FileInfo(name);
                                clipSize += info.Length;
                            }
                        }
                        else
                        {
                            info = new FileInfo(stringElement);
                            clipSize += info.Length;
                        }
                    }
                    bool goforw = true;
                    if (clipSize >= 104857600) //100 MB
                    {
                        MessageBoxResult mr = System.Windows.MessageBox.Show("Ciò che si sta tentando di inviare ha una dimensione superiore ai 100MB ed è richiesto diverso tempo per il completamento dell'operazione, si è sicuri di voler procedere?", "Attenzione!", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
                        if (mr == MessageBoxResult.Yes)
                        {

                        }
                        else
                        {
                            goforw = false;
                        }
                    }
                    if ((scl[0].Contains(pathS) && isclient) || !goforw)
                    {
                        //se il pc passivo si trova a mandare una cosa che è stata ricevuta
                        //non manda perchè è appena stato ricevuto
                    }
                    else
                    {
                        StringEnumerator sel = scl.GetEnumerator();
                        string path2 = System.IO.Path.Combine(pathS, Path.GetRandomFileName());
                        while (Directory.Exists(path2))
                        {//riprova se esiste già fino a quando non ne trovi una che non esiste
                            path2 = System.IO.Path.Combine(pathS, Path.GetRandomFileName());
                        }

                        Directory.CreateDirectory(path2);
                        path2 = System.IO.Path.Combine(path2, "archivio.zip");
                        FileStream fsOut = File.Create(path2);
                        zipStream = new ZipOutputStream(fsOut);

                        if (work_)
                        {
                            try
                            {
                                while (sel.MoveNext())
                                {
                                    if (!work_)
                                    {
                                        //sarebbe meglio non fargli fare il save sempre!! (soluzione?)
                                        break;
                                    }
                                    var curr = (sel.Current);
                                    var isDir = (File.GetAttributes(curr) & FileAttributes.Directory) == FileAttributes.Directory;
                                    FileInfo fi = new FileInfo(curr);
                                    if (isDir)
                                    {
                                        int folderOffset = curr.Length + (curr.EndsWith("\\") ? 0 : 1);
                                        CompressFolder(curr, zipStream, folderOffset, null);
                                    }
                                    else
                                    {
                                        string entryName = fi.Name; // Makes the name in zip based on the folder
                                        entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                                        ZipEntry newEntry = new ZipEntry(entryName);
                                        newEntry.DateTime = fi.LastWriteTime; // Note the zip format stores 2 second granularity

                                        newEntry.Size = fi.Length;

                                        zipStream.PutNextEntry(newEntry);

                                        // Zip the file in buffered chunks
                                        // the "using" will close the stream even if an exception occurs
                                        byte[] buffer = new byte[4096];
                                        using (FileStream streamReader = File.OpenRead(curr))
                                        {
                                            try
                                            {
                                                ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(streamReader, zipStream, buffer);
                                            }
                                            catch (Exception)
                                            {
                                                System.Windows.MessageBox.Show("Impossibile condividere i files copiati attualmente nella clipboard");
                                            }
                                            finally
                                            {
                                                streamReader.Close();
                                                streamReader.Dispose();
                                            }
                                        }
                                        zipStream.CloseEntry();
                                    }
                                }
                                zipStream.IsStreamOwner = true; // Makes the Close also Close the underlying stream
                                zipStream.Close();
                                zipStream.Dispose();
                                fsOut.Close();
                                fsOut.Dispose();
                            }

                            catch (Exception)
                            {
                                fsOut.Close();
                                fsOut.Dispose();
                                filesendingclosed.Set();
                            }

                        }


                        ///allora non procede con l'invio
                        ///-->possibilità notifica avviso utente
                        if (work_)
                        {
                            if (Monitor.TryEnter(SendStop))
                            {
                                bool wasclosed = false;
                                FileStream fs = File.OpenRead(path2);
                                try
                                {
                                    //send dimension
                                    ulong lf = (ulong)fs.Seek(0, SeekOrigin.End);
                                    byte[] bb = new byte[8];
                                    bb = BitConverter.GetBytes(lf);
                                    ns.Write(bb, 0, bb.Length);

                                    fs.Seek(0, SeekOrigin.Begin);
                                    //start send File
                                    int i = 0;
                                    while ((i = fs.Read(sendbuff, 0, sendbuff.Length)) > 0)
                                    {
                                        ns.Write(sendbuff, 0, i);
                                    }
                                }
                                catch (Exception)
                                {
                                    fs.Close();
                                    fs.Dispose();
                                    wasclosed = true;
                                    filesendingclosed.Set();
                                    System.Windows.MessageBox.Show("ATTENZIONE: dati non inviati");
                                }
                                finally
                                {
                                    if (!wasclosed)
                                    {
                                        fs.Close();
                                        fs.Dispose();
                                    }
                                    Monitor.Exit(SendStop);
                                }
                            }

                        }
                    }
                }
                catch (Exception e)
                {
                    System.Windows.MessageBox.Show("ERRORE nell'invio dei dati");
                }
                finally
                {

                    Monitor.Exit(monSend);
                }
            }

        }

        public void SendText()
        {
            if (Monitor.TryEnter(monSend))
            {
                try
                {
                    NetworkStream ns = client.GetStream();

                    Monitor.Enter(monClip);
                    String testo = System.Windows.Clipboard.GetText(TextDataFormat.Text);
                    Monitor.Exit(monClip);

                    MemoryStream mSS = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(testo));
                    mSS.Seek(0, SeekOrigin.Begin);
                    int len = 0;
                    if (mSS.Length < sendbuff.Length)
                        len = (int)mSS.Length;
                    else
                        len = sendbuff.Length;

                    //prende la lunghezza dello stream e la manda all'host remoto
                    ulong lf = (ulong)len;
                    byte[] bb = new byte[8];
                    bb = BitConverter.GetBytes(lf);
                    ns.Write(bb, 0, bb.Length);

                    //start send 
                    int i = 0;
                    while ((i = mSS.Read(sendbuff, 0, len)) > 0)
                    {
                        ns.Write(sendbuff, 0, i);
                    }

                }
                catch (Exception e)
                {
                    System.Windows.MessageBox.Show("ERRORE nell'invio del testo");
                }
                finally
                {
                    Monitor.Exit(monSend);
                }
            }

        }

        public void SendImg()
        {
            if (Monitor.TryEnter(monSend))
            {
                try
                {
                    NetworkStream ns = client.GetStream();

                    Monitor.Enter(monClip);
                    System.Windows.IDataObject iDO = System.Windows.Clipboard.GetDataObject();
                    Monitor.Exit(monClip);

                    //path = System.IO.Path.Combine(path, "FileIncollato.png");
                    System.Windows.Interop.InteropBitmap interB = (System.Windows.Interop.InteropBitmap)iDO.GetData(System.Windows.DataFormats.Bitmap);
                    BitmapSource bSource = interB as BitmapSource;

                    MemoryStream memStream = new MemoryStream();
                    BitmapEncoder bEncoder = new BmpBitmapEncoder();        //nel caso di immagini bisogna estrarre l'IDataObject
                    bEncoder.Frames.Add(BitmapFrame.Create(bSource));       //poi codificare i dati da una fonte (BitmapSource) con un codificatore bitmap,
                    bEncoder.Save(memStream);                               //memorizzarli in uno stream


                    //prende la lunghezza dello stream e la manda all'host remoto
                    ulong lf = (ulong)memStream.Seek(0, SeekOrigin.End); ;
                    byte[] bb = new byte[8];
                    bb = BitConverter.GetBytes(lf);
                    ns.Write(bb, 0, bb.Length);

                    memStream.Seek(0, SeekOrigin.Begin);
                    int i = 0;
                    while ((i = memStream.Read(sendbuff, 0, sendbuff.Length)) > 0)
                    {
                        ns.Write(sendbuff, 0, i);
                    }

                }
                catch (Exception e)
                {
                    System.Windows.MessageBox.Show("ERRORE nell'invio dell'immagine");
                }
                finally
                {
                    Monitor.Exit(monSend);
                }
            }

        }

        public void closeMngr(out bool closed)
        {
            bool devochiudere = true;
            bool wassendingfile = false;
            //entro in chiusura solo se il boolean work_ è a true:
            if (work_) //è ancora attivo
            {
                //qui controllo che non vi sia un invio in corso
                if (Monitor.TryEnter(monSend) == false)
                {

                    // stavo mandando
                    MessageBoxResult mr = System.Windows.MessageBox.Show("E' in corso un invio di dati, vuoi realmente interrompere la connessione?\n (è consigliato riprovare tra pochi istanti)", "Attenzione!", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
                    if (mr == MessageBoxResult.Yes)
                    {
                        work_ = false;
                        //interrompi il thread di invio e/o forza la connessione della chiusura
                        //devochiudere resta true;
                        wassendingfile = true;
                        if (zipStream != null)
                        {
                            zipStream.IsStreamOwner = true; // Makes the Close also Close the underlying stream
                            try
                            {
                                zipStream.Close();
                                zipStream.Dispose();
                            }
                            catch (Exception e)
                            {
                            }
                        }
                    }
                    else
                    {
                        //non devo chiudere, devo attendere e riprovare in seguito
                        devochiudere = false;
                    }

                }
                else
                {
                    Monitor.Exit(monSend);
                }


                if (!icos)
                {
                    //chiudo lo stream solo quando ho finito di ricevere
                    if (client != null)
                    {
                        if (client.GetStream().DataAvailable)
                        {//true solo se lo stream è in ricezione (testato in debug)
                             MessageBoxResult mr = System.Windows.MessageBox.Show("E' in corso una ricezione di dati, vuoi realmente interrompere la connessione?\n (è consigliato riprovare tra pochi istanti)", "Attenzione!", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
                             if (mr == MessageBoxResult.Yes)
                             {
                                 devochiudere = true;
                             }
                             else
                             {
                                 devochiudere = false;
                             }
                        }
                    }
                    
                }

                if (devochiudere)//se è vero ho deciso di interrompere l'invio
                {
                    try
                    {
                        work_ = false;


                        if(client!=null) {

                            if(client.Connected)
                            {
                                client.GetStream().Close();   
                            }
                            client.Close();
                            client = null;
                        }

                        if (wassendingfile)
                        {
                            filesendingclosed.WaitOne();
                        }
                        //attendo che dopo la chiusura vengano chiusi i file per la ricezione
                        fileclosed.WaitOne();

                        if (Directory.Exists(pathS))
                        {
                            foreach (var y in Directory.GetDirectories(pathS))
                            {
                                Directory.Delete(y, true);
                            }
                            foreach (var x in Directory.GetFiles(pathS))
                            {
                                File.Delete(x);
                            }

                            Directory.Delete(pathS);
                        }
                    }
                    catch (Exception e)
                    {
                    }
                    finally
                    {
                    }
                }

            }
            closed = devochiudere;
        }

        private void CompressFolder(string path, ZipOutputStream zipStream, int folderOffset, string subfol)
        {

            string[] files = Directory.GetFiles(path, "*.*");
            FileInfo cartella = new FileInfo(path);

            foreach (string filename in files)
            {
                FileInfo fi = new FileInfo(filename);

                string entryName;
                if (subfol == null)
                    entryName = cartella.Name + "/" + fi.Name;
                else
                    entryName = subfol + "/" + cartella.Name + "/" + fi.Name;

                //string entryName = filename.Substring(folderOffset); // Makes the name in zip based on the folder
                //entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                ZipEntry newEntry = new ZipEntry(entryName);
                newEntry.DateTime = fi.LastWriteTime; // Note the zip format stores 2 second granularity

                newEntry.Size = fi.Length;

                zipStream.PutNextEntry(newEntry);

                byte[] buffer = new byte[4096];
                FileStream streamReader = File.OpenRead(filename);

                try
                {

                    ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(streamReader, zipStream, buffer);
                }
                catch (Exception)
                {

                }
                finally
                {
                    streamReader.Close();
                    streamReader.Dispose();
                }

                zipStream.CloseEntry();
            }
            string[] folders = Directory.GetDirectories(path);

            foreach (string folder in folders)
            {
                string sEntry;
                FileInfo finfo = new FileInfo(folder);
                if (subfol == null)
                    sEntry = cartella.Name + "/" + finfo.Name + "\\";
                else
                    sEntry = subfol + "/" + cartella.Name + "/" + finfo.Name + "\\";

                sEntry = ZipEntry.CleanName(sEntry);
                ZipEntry zeOutput = new ZipEntry(sEntry);
                zipStream.PutNextEntry(zeOutput);
                zipStream.CloseEntry();

                if (subfol == null)
                {
                    CompressFolder(folder, zipStream, folderOffset, cartella.Name);
                }
                else
                {
                    CompressFolder(folder, zipStream, folderOffset, subfol + "/" + cartella.Name);
                }
            }
        }
    }

}
