using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;
using Windows.Devices.Gpio;
using Windows.Foundation;

// Il modello di elemento Pagina vuota è documentato all'indirizzo https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x410

namespace The_ABISS___A_Bus_Identity_Scanner_System
{
    /// <summary>
    /// Pagina vuota che può essere usata autonomamente oppure per l'esplorazione all'interno di un frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const String SUBSCRIPTION_FACE_DETECTION_CODE = "83dc340725b94488892c8e84ccbd170b";
        private static MediaCapture _mediaCapture;
        private bool isDetecting = false, isCapturing = false;
        List<User> Users, SuspectedUsers;
        SpeechRecognizer sr;
        SpeechSynthesizer ss;
        GpioPin pin = null;
        private const int LED_PIN = 5;
        private Func<Task> Service;

        public readonly int ticketLifeTime = 3;

        public MainPage()
        {
            this.InitializeComponent();
            Users = new List<User>();
            SuspectedUsers = new List<User>();
            Service = ServicePinNull;
            InitializeSpeechSynthesizer();
            InitializeSpeechRecognizer();
            InitializeGPIO();
            startCamera(null, null);
            cyclesController();
        }

        //GENERAL PURPOSE FUNCTION
        private string GenerateUniqueId()
        {
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var stringChars = new char[8];
            var random = new Random();
            User u = new User();
            string uniqueId;
            do
            {

                for (int i = 0; i < stringChars.Length; i++)
                {
                    stringChars[i] = chars[random.Next(chars.Length)];
                }
                uniqueId = new string(stringChars);
                u.uniqueId = uniqueId;

            } while (Users.Contains(u));

            return uniqueId;
        }
        private async Task<User> TakePhoto(String filename)
        {
            var photoFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting);
            await _mediaCapture.CapturePhotoToStorageFileAsync(ImageEncodingProperties.CreateBmp(), photoFile);

            IRandomAccessStream photoStream = await photoFile.OpenReadAsync();
            BitmapImage bitmap = new BitmapImage();
            bitmap.SetSource(photoStream);
            Image img = new Image() { Width = 300, Source = bitmap };

            //Add photo to grid

            photoContainer.Children.Add(img);
            Grid.SetRow(img, photoContainer.RowDefinitions.Count - 1);
            Grid.SetColumn(img, 0);

            User u = new User();
            u.PhotoFace = img;
            return u;
        }
        public async Task<byte[]> ReadFile(StorageFile file)
        {
            byte[] fileBytes = null;
            using (IRandomAccessStreamWithContentType stream = await file.OpenReadAsync())
            {
                fileBytes = new byte[stream.Size];
                using (DataReader reader = new DataReader(stream))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(fileBytes);
                }
            }

            return fileBytes;
        }
        async Task<List<String>> MakeRequestFaceDetect(StorageFile photoFile)
        {
            var client = new HttpClient();

            // Request headers
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", SUBSCRIPTION_FACE_DETECTION_CODE);

            var uri = "https://westcentralus.api.cognitive.microsoft.com/face/v1.0/detect?";

            HttpResponseMessage response;

            // Request body
            byte[] byteData = await ReadFile(photoFile);
            using (var content = new ByteArrayContent(byteData))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response = await client.PostAsync(uri, content);
            }
            if (response.IsSuccessStatusCode == false) throw new Exception("Autenticazione Fallita");
            String json, faceId, faceRectTop, faceRectLeft, faceRectWidth, faceRectHeight;
            json = await response.Content.ReadAsStringAsync();
            List<String> listOfFacesIdAndRect = new List<string>();
            string[] faces = json.Split(new string[] { "\"faceId\":\"" }, StringSplitOptions.None);
            for (int i = 1; i<faces.Length; i++) //i=1 because the first element is rubbish and some brackets...
            {
                faceId = faces[i].Substring(0, faces[i].IndexOf("\""));
                faceRectTop = faces[i].Substring(faces[i].IndexOf("\"top\":") + ("\"top\":").Length);
                faceRectTop = faceRectTop.Substring(0, faceRectTop.IndexOf(","));
                faceRectLeft = faces[i].Substring(faces[i].IndexOf("\"left\":") + ("\"left\":").Length);
                faceRectLeft = faceRectLeft.Substring(0, faceRectLeft.IndexOf(","));
                faceRectWidth = faces[i].Substring(faces[i].IndexOf("\"width\":") + ("\"width\":").Length);
                faceRectWidth = faceRectWidth.Substring(0, faceRectWidth.IndexOf(","));
                faceRectHeight = faces[i].Substring(faces[i].IndexOf("\"height\":") + ("\"height\":").Length);
                faceRectHeight = faceRectHeight.Substring(0, faceRectHeight.IndexOf("}"));
                listOfFacesIdAndRect.Add(faceId + "_" + faceRectTop + "_" + faceRectLeft + "_" + faceRectWidth + "_" + faceRectHeight);
            }
            await Say(((listOfFacesIdAndRect.Count==1) ? "Rilevato un utente" : "Rilevati "+listOfFacesIdAndRect.Count + " utenti") + " a bordo");
            return listOfFacesIdAndRect;

        }
        async Task<List<String>> MakeRequestFindSimilar(String faceIdTarget, List<String> faceIds)
        {
            var client = new HttpClient();

            // Request headers
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", SUBSCRIPTION_FACE_DETECTION_CODE);
            String faceIdsToString = "";
            foreach (String s in faceIds)
            {
                faceIdsToString += "\"" + s + "\",";
            }
            var uri = "https://westcentralus.api.cognitive.microsoft.com/face/v1.0/findsimilars?";

            HttpResponseMessage response;

            // Request body
            byte[] byteData = Encoding.UTF8.GetBytes("" +
                "{" +
                "\"faceId\":\"" + faceIdTarget + "\"," +
                "\"faceIds\":[" + faceIdsToString + "]" +
                "}"
                );

            using (var content = new ByteArrayContent(byteData))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                response = await client.PostAsync(uri, content);
            }

            if (response.IsSuccessStatusCode == false) throw new Exception("ERROR: " + response.ToString());
            String faceIdFound;
            faceIdFound = await response.Content.ReadAsStringAsync();

            List<String> faceIdsFound = new List<string>();
            while (faceIdFound.IndexOf("\"faceId\":\"") > 0)
            {
                faceIdFound = faceIdFound.Substring(faceIdFound.IndexOf("\"faceId\":\"") + ("\"faceId\":\"").Length);
                faceIdsFound.Add(faceIdFound.Substring(0, faceIdFound.IndexOf("\"")));
            }
            return faceIdsFound;

        }
        public async Task Say(String x)
        {
            txtInfo.Text += x + "\n";
            await Talk(x);
            await Task.Delay(x.Length * 80);
            txtInfo.Text += "☺";
        }
        public void Print(String x)
        {
            txtInfo.Text += x + "\n";
        }
        private async Task Talk(string message)
        {
            var stream = await ss.SynthesizeTextToStreamAsync(message);
            mediaElement.SetSource(stream, stream.ContentType);
            //mediaElement.Play();

        }
        public void Clear()
        {
            txtInfo.Text = "";
        }
        public void ResetAll()
        {
            photoContainer.Children.Clear();
        }
        public async Task GarbageCollector()
        {
            List<User> tempUsers = new List<User>();
            foreach (User u in Users)
            {
                if (u.expirationDate < DateTime.Now)
                {
                    try
                    {
                        var file = await ApplicationData.Current.TemporaryFolder.GetFileAsync(u.uniqueId);
                        await file.DeleteAsync();
                    }
                    catch (Exception e) { }
                }
                else
                    tempUsers.Add(u);
            }
            Users = tempUsers;
        }

        //INITIALIZERS

        private async void InitializeSpeechRecognizer()
        {
            try
            {
                sr = new SpeechRecognizer();

                //These for using XML SRGS file
                //StorageFile grammarContentFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///GrammarStructure.xml"));
                //SpeechRecognitionGrammarFileConstraint grammarConstraint = new SpeechRecognitionGrammarFileConstraint(grammarContentFile);
                //sr.Constraints.Add(grammarConstraint);

                await sr.CompileConstraintsAsync();
                Print("Riconoscitore Vocale Inizializzato!");

            }
            catch (Exception e)
            {
                Print(e.ToString());
            }
        }

        private async void InitializeSpeechSynthesizer()
        {
            ss = new SpeechSynthesizer();
            mediaElement.AutoPlay = true;
        }

        private async void InitializeGPIO()
        {
            var gpio = GpioController.GetDefault();

            // Show an error if there is no GPIO controller
            if (gpio == null)
            {
                pin = null;
                Print("Non ci sono controller GPIO.");
                return;
            }

            pin = gpio.OpenPin(LED_PIN);
            pin.Write(GpioPinValue.High);
            pin.SetDriveMode(GpioPinDriveMode.Output);

            Print("GPIO pin inizializzati.");
            Service = ServicePinTurn;
        }


        //BUTTONS & PROCEDURES

        private async void startCamera(object sender, RoutedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync();
            cePreview.Source = _mediaCapture;
            await _mediaCapture.StartPreviewAsync();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            Clear();
        }

        ///<summary>
        ///    Registers User. 
        ///</summary>
        private async Task Register(User u)
        {
            try
            {
                if (isCapturing == false) throw new Exception("Telecamera non inizializzata");
                if (isDetecting == true) throw new Exception("Riconoscimento facciale già in corso.");
                isDetecting = true;

                //if (biglietto valido) then
                Print("Inizio Registrazione...");
                u.expirationDate = DateTime.Now.AddMinutes(ticketLifeTime);
                Users.Add(u);
                ResetAll();
                photoContainer.Children.Add(u.PhotoFace);
                Grid.SetRow(u.PhotoFace, photoContainer.RowDefinitions.Count - 1);
                Grid.SetColumn(u.PhotoFace, photoContainer.ColumnDefinitions.Count - 1);
                Print("Convalida '" + u.uniqueId + "';");
                await Say("Biglietto convalidato!");
                ResetAll();
            }
            catch (Exception ex)
            {
                Print(ex.Message);
            }
            finally
            {
                isDetecting = false;
                ResetAll();
            }
        }

        ///<summary>
        ///    Authenticate multiple users
        ///</summary>

        private async Task<User> Authenticate()
        {
            User tempPhoto = null;
            User targetUser = null;
            var txt = new TextBlock();
            bool neverBroken = true;
            try
            {
                if (isCapturing == false) throw new Exception("Need to start Capturing");
                if (isDetecting == true) throw new Exception("Detection is already running!");
                isDetecting = true;

                string uniqueId = GenerateUniqueId();
                tempPhoto = await TakePhoto(uniqueId);
                tempPhoto.uniqueId = uniqueId;
                List<String> faceId_rectList = await MakeRequestFaceDetect(await ApplicationData.Current.TemporaryFolder.GetFileAsync(tempPhoto.uniqueId));
                foreach(string faceId_rect in faceId_rectList)
                {
                    tempPhoto.FaceId =faceId_rect.Split('_')[0];
                    Print("Ricerca volto corrispettivo...");
                    List<String> faceIds = new List<string>();
                    if (Users.Count > 0)
                    {
                        foreach (User u in Users)
                            faceIds.Add(u.FaceId);
                        List<String> foundFaceIds = await MakeRequestFindSimilar(tempPhoto.FaceId, faceIds);
                        Print("Match con l'utente...");
                        foreach (User u in Users)
                            foreach (String foundFaceId in foundFaceIds)
                                if (u.FaceId == foundFaceId)
                                {
                                    targetUser = u;
                                    break;
                                }
                    }
                    Image originalImage = new Image() { Width = 300, Source = tempPhoto.PhotoFace.Source };
                    RectangleGeometry geo = new RectangleGeometry();
                    geo.Rect = new Rect(int.Parse(faceId_rect.Split('_')[2])*15/32, int.Parse(faceId_rect.Split('_')[1])*15/32, int.Parse(faceId_rect.Split('_')[3])*15/32, int.Parse(faceId_rect.Split('_')[4])*15/32);
                    tempPhoto.PhotoFace.Clip = geo;

                    if (targetUser == null) {
                        await Say("ATTENZIONE! VOLTO NON AUTENTICATO! INSERISCI BIGLIETTO E PRONUNCIA 'BIGLIETTO INSERITO'");
                        neverBroken = false;
                        break;
                    }
                    else
                    {
                        photoContainer.Children.Add(targetUser.PhotoFace);
                        Grid.SetRow(targetUser.PhotoFace, photoContainer.RowDefinitions.Count - 1);
                        Grid.SetColumn(targetUser.PhotoFace, photoContainer.ColumnDefinitions.Count - 1);
                        txt.HorizontalAlignment = HorizontalAlignment.Center;
                        txt.VerticalAlignment = VerticalAlignment.Bottom;
                        txt.Text = "Durata residua biglietto: " + targetUser.expirationDate.Subtract(DateTime.Now).ToString();
                        photoContainer.Children.Add(txt);
                        Grid.SetRow(txt, photoContainer.RowDefinitions.Count - 1);
                        Grid.SetColumn(txt, photoContainer.ColumnDefinitions.Count - 1);
                        await Say("Autenticato! Grazie!");
                        await Task.Delay(1000);
                        ResetAll();
                        tempPhoto.PhotoFace = originalImage;
                        photoContainer.Children.Add(tempPhoto.PhotoFace);
                        Grid.SetRow(tempPhoto.PhotoFace, 0);
                        Grid.SetColumn(tempPhoto.PhotoFace, 0);
                        targetUser = null;
                    }
                }
                var file = await ApplicationData.Current.TemporaryFolder.GetFileAsync(tempPhoto.uniqueId);
                await file.DeleteAsync();
                ResetAll();
                if (neverBroken) tempPhoto = null;
            }
            catch (Exception ex)
            {
                Print(ex.Message);
            }
            finally
            {
                isDetecting = false;
            }
            return tempPhoto;
        }

        private async Task OutlawHandler(User u)
        {
            if (u == null || u.FaceId == null)
            {
                SuspectedUsers.RemoveAll(usr => usr.expirationDate < DateTime.Now);
                return;
            }
            try
            {
                List<String> suspectedUsersIds = new List<string>();
                foreach (User user in SuspectedUsers) suspectedUsersIds.Add(user.FaceId);
                User targetUser = null;
                List<string> foundFaceIds = new List<string>();
                if (suspectedUsersIds.Count!=0)
                    foundFaceIds = await MakeRequestFindSimilar(u.FaceId, suspectedUsersIds);
                foreach (User user in SuspectedUsers)
                    foreach (String foundFaceId in foundFaceIds)
                        if (user.FaceId == foundFaceId)
                        {
                            targetUser = user;
                            break;
                        }
                if (targetUser == null) {
                    u.expirationDate = DateTime.Now.AddMinutes(ticketLifeTime/2);
                    u.state = User.State.SUSPECTED;
                    SuspectedUsers.Add(u);
                }
                else
                {
                    photoContainer.Children.Add(targetUser.PhotoFace);
                    Grid.SetRow(targetUser.PhotoFace, photoContainer.RowDefinitions.Count - 1);
                    Grid.SetColumn(targetUser.PhotoFace, photoContainer.ColumnDefinitions.Count - 1);
                    await Say("ATTENZIONE! STAI CONTRAVVENENDO ALLE REGOLE DEL TRASPORTO PUBBLICO! Sei stato multato!");
                    await Task.Delay(1000);
                    ResetAll();
                    SuspectedUsers.Remove(targetUser);
                }
            }
            catch (Exception e) { Print(e.ToString()); }



        }

        //EXECUTION

        private async void cyclesController()
        {
            var x = await ApplicationData.Current.TemporaryFolder.GetFilesAsync();
            foreach (StorageFile file in x) await file.DeleteAsync();
            
            await Say("Benvenuto su ABISS!");
            bool done = false;
            User user;
            while (true)
            {
                await Say("Guarda verso di me...");
                user = await Authenticate();
                try
                {
                    SpeechRecognitionResult speechRecognitionResult;
                    speechRecognitionResult = await sr.RecognizeAsync();
                    Clear();
                    await Say(speechRecognitionResult.Text.ToUpper());
                    foreach (string command in speechRecognitionResult.Text.ToLower().Split(' '))
                    {
                        switch (command)
                        {
                            case "convalida":
                            case "biglietto":
                            case "timbra":
                            case "biglietti":
                            case "inseriti":
                                Clear();
                                if (user!= null) await Register(user);
                                user = null;
                                done = true;
                                break;
                            default:
                                break;

                        }
                        if (done)
                        {
                            break;
                        }
                    }
                }
                catch (Exception e) {Print(e.Message); }
                finally {
                    done = false;
                    await OutlawHandler(user);
                    user = null;
                    await GarbageCollector();
                    await Task.Delay(2000);
                    Clear();
                }
            }
        }


        //SERVICES

        private async Task ServicePinTurn()
        {
            pin.Write(GpioPinValue.Low);
            await Task.Delay(10000);
            pin.Write(GpioPinValue.High);
        }

        private async Task ServicePinNull() { }
    }
}
