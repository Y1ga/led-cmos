using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp;
using ZWOptical.ASISDK;

namespace ASICamera_demo
{
    public static class SemaphoreHolder
    {
        // 初始化信号量，初始计数为 3，最大计数也为 3
        public static Semaphore set = new Semaphore(0, 1);
        public static Semaphore reset = new Semaphore(2, 2);
        public static Semaphore reset_mono = new Semaphore(1, 1);
        public static Semaphore refresh = new Semaphore(0, 1);
        public static Semaphore send = new Semaphore(0, 1);
        public static Semaphore protect_std = new Semaphore(1, 1);
        public static Semaphore protect_mono = new Semaphore(1, 1);
        public static Semaphore log = new Semaphore(0, 1);
        public static Semaphore pwm = new Semaphore(1, 1);
        public static Semaphore auto = new Semaphore(0, 1);

        // 定义读写锁
        public static ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();
        public static ReaderWriterLockSlim send_lock = new ReaderWriterLockSlim();

        // 标志位
        public static bool is_std = false;
        public static bool is_changed = false;
        public static bool is_mono = false;
        public static bool is_auto = false;
        public static bool is_search = false;
        public static bool is_mono_exp = false;

        // 最佳曝光计数
        public static int best_exp_count = 0;
        public static int tolerance_count = 0;
        public static int best_exp_count_tolerance = 3;
    }

    public class Monochromator
    {
        private int start_spectrum = 400;
        private int stride = 10;
        private int selected_spectrum = 400;
        private int interval_time = 10;

        // exp / min / max / mean / std
        private int[,] m_spec_exp = new int[31, 5];
        private int current_max_index;
        private int current_max_exp;

        public int Start_spectrum
        {
            get => start_spectrum;
            set => start_spectrum = value;
        }
        public int Stride
        {
            get => stride;
            set => stride = value;
        }
        public int Selected_spectrum
        {
            get => selected_spectrum;
            set => selected_spectrum = value;
        }
        public int Interval_time
        {
            get => interval_time;
            set => interval_time = value;
        }
        public int[,] Spec_exp
        {
            get => m_spec_exp;
            set => m_spec_exp = value;
        }
        public int Current_max_index
        {
            get => current_max_index;
            set => current_max_index = value;
        }
        public int Current_max_exp
        {
            get => current_max_exp;
            set => current_max_exp = value;
        }
    }

    public class LedArray
    {
        // 存储led最小/最大PWM时恰好曝光所需的曝光时间;min, max, mean, std灰度值
        private int[,] m_pwm_exp = new int[16, 5];

        private int current_min_led;
        private int current_max_led;
        private int current_min_exp;
        private int current_max_exp;

        private int best_exp;
        private byte[] index = new byte[16];
        private byte[] value = new byte[16];
        private byte selected_index = 2;
        private byte selected_value = 0;
        private byte stride = 1;

        public LedArray()
        {
            for (int i = 0; i < 16; i++)
            {
                Index[i] = (byte)(i + 1);
                Value[i] = 0;
            }
        }

        public byte[] Index
        {
            get => index;
            set => index = value;
        }
        public byte[] Value
        {
            get => value;
            set => this.value = value;
        }
        public byte Selected_index
        {
            get => selected_index;
            set => selected_index = value;
        }
        public byte Selected_value
        {
            get => selected_value;
            set => selected_value = value;
        }
        public byte Stride
        {
            get => stride;
            set => stride = value;
        }
        public int[,] Pwm_exp
        {
            get => m_pwm_exp;
            set => m_pwm_exp = value;
        }
        public int Best_exp
        {
            get => best_exp;
            set => best_exp = value;
        }

        public int Current_max_led
        {
            get => current_max_led;
            set => current_max_led = value;
        }

        public int Current_max_exp
        {
            get => current_max_exp;
            set => current_max_exp = value;
        }
        public int Current_min_led
        {
            get => current_min_led;
            set => current_min_led = value;
        }
        public int Current_min_exp
        {
            get => current_min_exp;
            set => current_min_exp = value;
        }
    }

    class Camera
    {
        public enum CaptureMode
        {
            Video = 0,
            Snap = 1,
        };

        // 最佳曝光值
        private int best_exp;

        /*线程锁*/
        uint is_std_state = 0;

        //保存图片名
        private static string datetime = Convert.ToString(
            DateTime.Now.ToString("yyyy-MM-dd-HH_mm")
        );
        private static string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "cmos_led"
        );
        private string selectedFolderPath = defaultPath + '\\' + Datetime;
        private string file_name;

        private string m_cameraName = "";
        private ASICameraDll2.ASI_IMG_TYPE m_imgType = ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8;

        // private ASICameraDll2.ASI_SN m_SN;
        private CaptureMode m_CaptureMode = CaptureMode.Video;
        private int m_iCameraID;
        private int m_iMaxWidth;
        private int m_iMaxHeight;
        private int m_iCurWidth;
        private int m_iCurHeight;
        private int m_iSize;
        private int m_iBin;
        private int[] m_supBins = new int[16];
        private ASICameraDll2.ASI_IMG_TYPE[] m_supVideoFormats = new ASICameraDll2.ASI_IMG_TYPE[8];
        private int m_iCurrentGainValue;
        private int m_iCurrentExpMs;
        private int m_iCurrentWBR;
        private int m_iCurrentWBB;
        private int m_iCurrentBandWidth;

        private int m_iTemperature;
        private int m_iCurrentOffset;
        private int m_iMaxGainValue;
        private int m_iMaxWBRValue;
        private int m_iMaxWBBValue;
        private int m_iMaxOffset;
        private bool m_bIsOpen = false;
        private bool m_bIsColor = false;
        private bool m_bIsCooler = false;
        private bool m_bIsUSB3 = false;
        private bool m_bIsUSB3Host = false;

        private bool m_bGainAutoChecked = false;
        private bool m_bExposureAutoChecked = false;
        private bool m_bWhiteBalanceAutoChecked = false;
        private bool m_bBandWidthAutoChecked = false;

        /*hist*/
        private double min_hist;
        private double max_hist;
        private double mean_hist;
        private double std_hist;

        private System.Timers.Timer m_timer = new System.Timers.Timer(500); // 实例化Timer类，设置间隔时间为1000毫秒
        Thread captureThread;

        public int getCurrentExpMs()
        {
            return m_iCurrentExpMs;
        }

        public int getCurrentGain()
        {
            return m_iCurrentGainValue;
        }

        public int getCurrentWBR()
        {
            return m_iCurrentWBR;
        }

        public int getCurrentWBB()
        {
            return m_iCurrentWBB;
        }

        public int getCurrentBandWidth()
        {
            return m_iCurrentBandWidth;
        }

        public int getCurrentOffset()
        {
            return m_iCurrentOffset;
        }

        public int getMaxOffset()
        {
            return m_iMaxOffset;
        }

        public int getMaxGain()
        {
            return m_iMaxGainValue;
        }

        public int getMaxWBR()
        {
            return m_iMaxWBRValue;
        }

        public int getMaxWBB()
        {
            return m_iMaxWBBValue;
        }

        public int getMaxWidth()
        {
            return m_iMaxWidth;
        }

        public int getMaxHeight()
        {
            return m_iMaxHeight;
        }

        public bool getIsColor()
        {
            return m_bIsColor;
        }

        public bool getIsCooler()
        {
            return m_bIsCooler;
        }

        public bool getIsUSB3()
        {
            return m_bIsUSB3;
        }

        public bool getIsUSB3Host()
        {
            return m_bIsUSB3Host;
        }

        public int[] getBinArr()
        {
            return m_supBins;
        }

        public ASICameraDll2.ASI_IMG_TYPE[] getImgTypeArr()
        {
            return m_supVideoFormats;
        }

        // Constructor
        public Camera()
        {
            captureThread = new Thread(new ThreadStart(run));
            m_timer.Elapsed += new System.Timers.ElapsedEventHandler(timeout); // 到达时间的时候执行事件
            m_timer.AutoReset = true; // 设置是执行一次（false）还是一直执行(true)
            m_timer.Start();
        }

        private void timeout(object source, System.Timers.ElapsedEventArgs e)
        {
            if (!m_bIsOpen)
                return;

            int iVal = 0;

            iVal = ASICameraDll2.ASIGetControlValue(
                m_iCameraID,
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_TEMPERATURE
            );
            PopupMessageBox("Get Temperature", iVal);

            if (m_bGainAutoChecked)
            {
                if (getControlValue(ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN, out iVal))
                {
                    PopupMessageBox("Gain Auto", iVal);
                }
            }
            if (m_bExposureAutoChecked)
            {
                if (getControlValue(ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE, out iVal))
                {
                    PopupMessageBox("Exposure Auto", iVal);
                }
            }

            if (m_bWhiteBalanceAutoChecked)
            {
                if (getControlValue(ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_B, out iVal))
                {
                    PopupMessageBox("White Balance Blue Auto", iVal);
                }
                if (getControlValue(ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_R, out iVal))
                {
                    PopupMessageBox("White Balance Red Auto", iVal);
                }
            }
            if (m_bBandWidthAutoChecked)
            {
                if (getControlValue(ASICameraDll2.ASI_CONTROL_TYPE.ASI_BANDWIDTHOVERLOAD, out iVal))
                {
                    PopupMessageBox("BandWidth Auto", iVal);
                }
            }
        }

        // camera Init
        private bool cameraInit()
        {
            ASICameraDll2.ASI_ERROR_CODE err;
            int cameraNum = ASICameraDll2.ASIGetNumOfConnectedCameras();
            if (cameraNum == 0)
            {
                PopupMessageBox("No Camera Connection");
                return false;
            }

            ASICameraDll2.ASI_CAMERA_INFO CamInfoTemp;
            ASICameraDll2.ASIGetCameraProperty(out CamInfoTemp, 0);

            for (int i = 0; i < 16; i++)
            {
                m_supBins[i] = 0;
            }
            int index = 0;
            while (CamInfoTemp.SupportedBins[index] != 0)
            {
                m_supBins[index] = CamInfoTemp.SupportedBins[index];
                index++;
            }

            for (int i = 0; i < 8; i++)
            {
                m_supVideoFormats[i] = ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_END;
            }
            index = 0;
            while (
                CamInfoTemp.SupportedVideoFormat[index] != ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_END
            )
            {
                m_supVideoFormats[index] = CamInfoTemp.SupportedVideoFormat[index];
                index++;
            }

            err = ASICameraDll2.ASIOpenCamera(m_iCameraID);
            if (err != ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return false;
            }

            err = ASICameraDll2.ASIInitCamera(m_iCameraID);
            if (err != ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return false;
            }

            int iCtrlNum;
            ASICameraDll2.ASI_CONTROL_CAPS CtrlCap;
            ASICameraDll2.ASIGetNumOfControls(m_iCameraID, out iCtrlNum);

            for (int i = 0; i < iCtrlNum; i++)
            {
                ASICameraDll2.ASIGetControlCaps(m_iCameraID, i, out CtrlCap);
                if (CtrlCap.ControlType == ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN)
                {
                    m_iMaxGainValue = CtrlCap.MaxValue;
                }
                else if (CtrlCap.ControlType == ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_R)
                {
                    m_iMaxWBRValue = CtrlCap.MaxValue;
                }
                else if (CtrlCap.ControlType == ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_B)
                {
                    m_iMaxWBBValue = CtrlCap.MaxValue;
                }
            }

            m_iCurrentGainValue = ASICameraDll2.ASIGetControlValue(
                m_iCameraID,
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN
            );
            m_iCurrentExpMs = ASICameraDll2.ASIGetControlValue(
                m_iCameraID,
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE
            );
            m_iCurrentWBB = ASICameraDll2.ASIGetControlValue(
                m_iCameraID,
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_B
            );
            m_iCurrentWBR = ASICameraDll2.ASIGetControlValue(
                m_iCameraID,
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_R
            );

            m_iCurrentBandWidth = ASICameraDll2.ASIGetControlValue(
                m_iCameraID,
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_BANDWIDTHOVERLOAD
            );
            m_iTemperature = ASICameraDll2.ASIGetControlValue(
                m_iCameraID,
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_TEMPERATURE
            );

            int startx = 0,
                starty = 0;
            m_iBin = 1;
            err = ASICameraDll2.ASISetROIFormat(
                m_iCameraID,
                CamInfoTemp.MaxWidth,
                CamInfoTemp.MaxHeight,
                m_iBin,
                m_imgType
            );
            if (err != ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return false;
            }
            else
            {
                ASICameraDll2.ASISetStartPos(m_iCameraID, startx, starty);
                ASICameraDll2.ASIGetStartPos(m_iCameraID, out startx, out starty);
            }

            return true;
        }

        public bool open()
        {
            if (!cameraInit())
            {
                m_bIsOpen = false;
                return false;
            }
            m_bIsOpen = true;
            return true;
        }

        public bool close()
        {
            ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2.ASICloseCamera(m_iCameraID);
            if (err != ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
                return false;
            stopCapture();
            m_bIsOpen = false;
            return true;
        }

        public void startCapture()
        {
            if (!m_bIsOpen)
                return;

            if (m_CaptureMode == CaptureMode.Video)
            {
                ASICameraDll2.ASIStartVideoCapture(m_iCameraID);
                startCaptureThread();
            }
            else if (m_CaptureMode == CaptureMode.Snap)
            {
                startCaptureThread();
            }
        }

        public void stopCapture()
        {
            if (!m_bIsOpen)
                return;
            if (m_CaptureMode == CaptureMode.Video)
            {
                stopCaptureThread();
                ASICameraDll2.ASIStopVideoCapture(m_iCameraID);
            }
            else if (m_CaptureMode == CaptureMode.Snap)
            {
                stopCaptureThread();
            }
        }

        public void switchMode(CaptureMode mode)
        {
            m_CaptureMode = mode;
        }

        public bool setControlValue(
            ASICameraDll2.ASI_CONTROL_TYPE type,
            int value,
            ASICameraDll2.ASI_BOOL bAuto
        )
        {
            SemaphoreHolder.rwLock.EnterWriteLock();
            SemaphoreHolder.is_changed = true;
            ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2.ASISetControlValue(
                m_iCameraID,
                type,
                value
            );

            if (err == ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
            {
                SemaphoreHolder.rwLock.ExitWriteLock();
                return true;
            }
            else
            {
                PopupMessageBox("Set Control Value Fail: " + err.ToString());
                SemaphoreHolder.rwLock.ExitWriteLock();
                return false;
            }
        }

        public bool setControlValueAuto(
            ASICameraDll2.ASI_CONTROL_TYPE type,
            int value,
            ASICameraDll2.ASI_BOOL bAuto
        )
        {
            ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2.ASISetControlValue(
                m_iCameraID,
                type,
                value
            );
            if (err == ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
            {
                if (bAuto == ASICameraDll2.ASI_BOOL.ASI_TRUE)
                {
                    switch (type)
                    {
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN:
                            m_bGainAutoChecked = true;
                            break;
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE:
                            m_bExposureAutoChecked = true;
                            break;
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_R:
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_B:
                            m_bWhiteBalanceAutoChecked = true;
                            break;
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_BANDWIDTHOVERLOAD:
                            m_bBandWidthAutoChecked = true;
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    switch (type)
                    {
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN:
                            m_bGainAutoChecked = false;
                            break;
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE:
                            m_bExposureAutoChecked = false;
                            break;
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_R:
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_WB_B:
                            m_bWhiteBalanceAutoChecked = false;
                            break;
                        case ASICameraDll2.ASI_CONTROL_TYPE.ASI_BANDWIDTHOVERLOAD:
                            m_bBandWidthAutoChecked = false;
                            break;
                        default:
                            break;
                    }
                }
                return true;
            }
            else
            {
                PopupMessageBox("Set Control Value Auto Fail: " + err.ToString());
                return false;
            }
        }

        public bool getControlValue(ASICameraDll2.ASI_CONTROL_TYPE type, out int iValue)
        {
            iValue = ASICameraDll2.ASIGetControlValue(m_iCameraID, type);
            /*            ASICameraDll2.ASI_ERROR_CODE err = ASI_ERROR_CODE.ASI_ERROR_BUFFER_TOO_SMALL;
                        if (err == ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
                        {
                            return true;
                        }
                        else
                        {
                            popupmessagebox("get control value fail: " + err.tostring());
                            return false;
                        }*/

            return true;
        }

        public string scan()
        {
            int cameraNum = ASICameraDll2.ASIGetNumOfConnectedCameras();
            // Consider only one camera connection
            if (cameraNum > 0)
            {
                ASICameraDll2.ASI_CAMERA_INFO camInfoTemp;
                ASICameraDll2.ASIGetCameraProperty(out camInfoTemp, 0);
                m_cameraName = camInfoTemp.Name;

                m_iCameraID = camInfoTemp.CameraID;
                m_cameraName = camInfoTemp.Name;
                m_iMaxWidth = camInfoTemp.MaxWidth;
                m_iMaxHeight = camInfoTemp.MaxHeight;
                m_bIsColor =
                    camInfoTemp.IsColorCam == ASICameraDll2.ASI_BOOL.ASI_TRUE ? true : false;
                m_bIsCooler =
                    camInfoTemp.IsCoolerCam == ASICameraDll2.ASI_BOOL.ASI_TRUE ? true : false;
                m_bIsUSB3 =
                    camInfoTemp.IsUSB3Camera == ASICameraDll2.ASI_BOOL.ASI_TRUE ? true : false;
                m_bIsUSB3Host =
                    camInfoTemp.IsUSB3Host == ASICameraDll2.ASI_BOOL.ASI_TRUE ? true : false;
            }
            else
            {
                m_cameraName = "";
            }
            return m_cameraName;
        }

        public bool setImageFormat(
            int width,
            int height,
            int startx,
            int starty,
            int bin,
            ASICameraDll2.ASI_IMG_TYPE type
        )
        {
            // 这样写可能还有线程不安全的隐患！！！
            // 特别是 SemaphoreHolder.is_changed = true
            // 它是开启相机前就预加载好的
            // 明天研究研究开启相机前它是怎么加载setcontrol 和 setroiformat的
            SemaphoreHolder.rwLock.EnterWriteLock();
            SemaphoreHolder.is_changed = true;
            bool bCanStartThread = false;
            if (!m_bThreadStop && m_bThreadRunning)
            {
                stopCapture();
                bCanStartThread = true;
            }

            ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2.ASISetROIFormat(
                m_iCameraID,
                width,
                height,
                bin,
                type
            );
            if (err != ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
            {
                /*
                    int iWidth,  the width of the ROI area. Make sure iWidth%8 == 0.
                    int iHeight,  the height of the ROI area. Make sure iHeight%2 == 0,
                    further, for USB2.0 camera ASI120, please make sure that iWidth*iHeight%1024=0.
                */
                //if(iWidth%8 !=0 || iHeight%2 != 0)
                //{
                //    MessageBox.Show("Wrong Resolution");
                //    return;
                //}

                PopupMessageBox("SetFormat Error: " + err.ToString());
                return false;
            }

            m_iCurWidth = width;
            m_iCurHeight = height;
            m_iSize = m_iMaxWidth * m_iMaxHeight;
            m_iBin = bin;
            m_imgType = type;

            /*            ASICameraDll2.ASISetStartPos(m_iCameraID, startx, starty);
                        ASICameraDll2.ASIGetStartPos(m_iCameraID, out startx, out starty);*/

            if (bCanStartThread)
            {
                startCapture();
            }
            SemaphoreHolder.rwLock.ExitWriteLock();
            return true;
        }

        #region RefreshUI delegate
        // RefreshUI delegate
        public delegate void RefreshUICallBack(Bitmap bmp);
        public delegate void RefreshHistogramCallBack(Bitmap bmp);
        public delegate void RefreshCaptureCallBack(Bitmap bmp, uint flag);
        private RefreshHistogramCallBack RefreshHistogram;
        private RefreshUICallBack RefreshUI;
        private RefreshCaptureCallBack RefreshCapture;
        private bool m_bThreadRunning = false;
        private bool m_bThreadStop = false;
        private bool m_bThreadExit = false;

        //hist
        IntPtr buffer = IntPtr.Zero;
        static int histogram_width = 270;
        static int histogram_height = 100;
        static int offset_y = 15;
        Mat hist = new Mat();

        public void SetRefreshUICallBack(RefreshUICallBack callBack)
        {
            RefreshUI = callBack;
        }

        public void SetRefreshHistogramCallBack(RefreshHistogramCallBack callBack)
        {
            RefreshHistogram = callBack;
        }

        public void SetRefreshCaptureCallBack(RefreshCaptureCallBack callBack)
        {
            RefreshCapture = callBack;
        }

        // MessageBox delegate
        public delegate void MessageBoxCallBack(string str, int iVal = 0);
        private MessageBoxCallBack PopupMessageBox;

        public double Min_hist
        {
            get => min_hist;
            set => min_hist = value;
        }
        public double Max_hist
        {
            get => max_hist;
            set => max_hist = value;
        }
        public double Mean_hist
        {
            get => mean_hist;
            set => mean_hist = value;
        }
        public double Std_hist
        {
            get => std_hist;
            set => std_hist = value;
        }

        public string SelectedFolderPath
        {
            get => selectedFolderPath;
            set => selectedFolderPath = value;
        }
        public static string Datetime
        {
            get => datetime;
            set => datetime = value;
        }
        public string File_name
        {
            get => file_name;
            set => file_name = value;
        }
        public ASICameraDll2.ASI_IMG_TYPE ImgType
        {
            get => m_imgType;
            set => m_imgType = value;
        }
        public uint Is_std_state
        {
            get => is_std_state;
            set => is_std_state = value;
        }
        public int Best_exp
        {
            get => best_exp;
            set => best_exp = value;
        }

        public void SetMessageBoxCallBack(MessageBoxCallBack callBack)
        {
            PopupMessageBox = callBack;
        }
        #endregion

        #region Capture thread
        // Capture thread
        public void startCaptureThread()
        {
            if (!m_bThreadRunning)
            {
                m_bThreadStop = false;
                captureThread.Start();
            }
            else
            {
                m_bThreadStop = false;
            }
        }

        public void stopCaptureThread()
        {
            m_bThreadStop = true;
        }

        public void exitCaptureThread()
        {
            m_bThreadExit = true;
        }

        public Bitmap updateHistogram(Mat hist)
        {
            Mat histogram = new Mat(
                histogram_height,
                histogram_width,
                MatType.CV_8UC3,
                new Scalar(255, 255, 255)
            );
            if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8)
            {
                int bin_w = (int)Math.Round((double)histogram_width / 256);
                Cv2.Normalize(
                    hist,
                    hist,
                    0,
                    histogram.Rows - offset_y,
                    NormTypes.MinMax,
                    -1,
                    new Mat()
                );

                for (int i = 1; i < 256; i++)
                {
                    Cv2.Line(
                        histogram,
                        new OpenCvSharp.Point(
                            bin_w * (i - 1),
                            histogram_height - offset_y - (int)Math.Round(hist.At<float>(0, i - 1))
                        ),
                        new OpenCvSharp.Point(
                            bin_w * i,
                            histogram_height - offset_y - (int)Math.Round(hist.At<float>(0, i))
                        ),
                        new Scalar(0, 0, 0),
                        2,
                        LineTypes.Link8,
                        0
                    );
                }
                Cv2.PutText(
                    histogram,
                    "0",
                    new OpenCvSharp.Point(0, histogram_height - 20),
                    HersheyFonts.HersheySimplex,
                    0.3,
                    new Scalar(0, 0, 0),
                    1
                );
                int step = 250 / 5;
                for (int i = 1; i < 5; i++)
                {
                    int value = i * step;
                    string label = value.ToString();
                    Cv2.PutText(
                        histogram,
                        label,
                        new OpenCvSharp.Point(bin_w * (i * step), histogram_height - 20),
                        HersheyFonts.HersheySimplex,
                        0.3,
                        new Scalar(0, 0, 0),
                        1
                    );
                }
                Cv2.PutText(
                    histogram,
                    "255",
                    new OpenCvSharp.Point(256, histogram_height - 20),
                    HersheyFonts.HersheySimplex,
                    0.3,
                    new Scalar(0, 0, 0),
                    1
                );
            }
            else if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
            {
                int bin_w = (int)Math.Round((double)histogram_width / 256);
                int sampleStep = 65536 / 256;
                Mat sampledHist = new Mat(256, 1, MatType.CV_32FC1);

                for (int i = 0; i < 256; i++)
                {
                    float sum = 0;
                    for (int j = 0; j < sampleStep; j++)
                    {
                        sum += hist.At<float>(i * sampleStep + j);
                    }
                    sampledHist.Set<float>(i, 0, sum / sampleStep);
                }

                Cv2.Normalize(
                    sampledHist,
                    sampledHist,
                    0,
                    sampledHist.Rows - offset_y,
                    NormTypes.MinMax,
                    -1,
                    null
                );

                for (int i = 1; i < 256; i++)
                {
                    Cv2.Line(
                        histogram,
                        new OpenCvSharp.Point(
                            bin_w * (i - 1),
                            histogram_height
                                - offset_y
                                - (int)Math.Round(sampledHist.At<float>(0, i - 1))
                        ),
                        new OpenCvSharp.Point(
                            bin_w * i,
                            histogram_height
                                - offset_y
                                - (int)Math.Round(sampledHist.At<float>(0, i))
                        ),
                        new Scalar(0, 0, 0),
                        2,
                        LineTypes.Link8,
                        0
                    );
                }
                Cv2.PutText(
                    histogram,
                    "0",
                    new OpenCvSharp.Point(0, histogram_height - 20),
                    HersheyFonts.HersheySimplex,
                    0.3,
                    new Scalar(0, 0, 0),
                    1
                );
                int step = 250 / 5;
                for (int i = 1; i < 5; i++)
                {
                    int value = i * (65535 / 5);
                    string label = value.ToString();
                    Cv2.PutText(
                        histogram,
                        label,
                        new OpenCvSharp.Point(bin_w * (i * step), histogram_height - 20),
                        HersheyFonts.HersheySimplex,
                        0.3,
                        new Scalar(0, 0, 0),
                        1
                    );
                }
                Cv2.PutText(
                    histogram,
                    "65535",
                    new OpenCvSharp.Point(256, histogram_height - 20),
                    HersheyFonts.HersheySimplex,
                    0.3,
                    new Scalar(0, 0, 0),
                    1
                );
            }
            return new Bitmap(OpenCvSharp.Extensions.BitmapConverter.ToBitmap(histogram));
        }

        public void run()
        {
            m_bThreadRunning = true;

            while (true)
            {
                if (m_bThreadExit)
                {
                    break;
                }
                if (m_bThreadStop)
                {
                    continue;
                }
                if (SemaphoreHolder.is_std)
                {
                    {
                        SemaphoreHolder.set.WaitOne();
                        SemaphoreHolder.rwLock.EnterReadLock();
                        int cameraID = m_iCameraID;
                        int width = m_iCurWidth;
                        int height = m_iCurHeight;
                        int buffersize = 0;
                        if (
                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                            || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                        )
                            buffersize = width * height;
                        if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                            buffersize = width * height * 2;
                        if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24)
                            buffersize = width * height * 3;
                        buffer = Marshal.AllocCoTaskMem(buffersize);

                        if (m_CaptureMode == CaptureMode.Video)
                        {
                            int expMs;
                            expMs = ASICameraDll2.ASIGetControlValue(
                                cameraID,
                                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE
                            );
                            ASICameraDll2.ASISetControlValue(
                                m_iCameraID,
                                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE,
                                expMs
                            );
                            expMs /= 1000;
                            ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2
                                .ASI_ERROR_CODE
                                .ASI_ERROR_INVALID_ID;
                            for (int i = 0; i < 5; i++)
                            {
                                err = ASICameraDll2.ASIGetVideoData(
                                    cameraID,
                                    buffer,
                                    buffersize,
                                    expMs * 2 + 500
                                );
                            }
                            if (err == ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
                            {
                                if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8)
                                {
                                    Mat buffer_mat = new Mat(
                                        height,
                                        width,
                                        MatType.CV_8UC1,
                                        buffer
                                    );
                                    Cv2.CalcHist(
                                        new Mat[] { buffer_mat },
                                        new int[] { 0 },
                                        new Mat(),
                                        hist,
                                        1,
                                        new int[] { 256 },
                                        new Rangef[] { new Rangef(0, 256) },
                                        uniform: true
                                    );
                                    Scalar mean_scalar,
                                        std_scalar;
                                    Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                    Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                    mean_hist = mean_scalar[0];
                                    std_hist = std_scalar[0];
                                }
                                else if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                                {
                                    Mat buffer_mat = new Mat(
                                        height,
                                        width,
                                        MatType.CV_16UC1,
                                        buffer
                                    );
                                    Cv2.CalcHist(
                                        new Mat[] { buffer_mat },
                                        new int[] { 0 },
                                        new Mat(),
                                        hist,
                                        1,
                                        new int[] { 65536 },
                                        new Rangef[] { new Rangef(0, 65536) },
                                        uniform: true
                                    );
                                    Scalar mean_scalar,
                                        std_scalar;
                                    Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                    Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                    mean_hist = mean_scalar[0];
                                    std_hist = std_scalar[0];
                                }
                                byte[] byteArray = new byte[buffersize];
                                Marshal.Copy(buffer, byteArray, 0, buffersize);

                                Marshal.FreeCoTaskMem(buffer);
                                Bitmap bmp = new Bitmap(width, height);

                                int index = 0;

                                var lockBitmap = new LockBitmap(bmp);
                                lockBitmap.LockBits();
                                for (int i = 0; i < height; i++)
                                {
                                    for (int j = 0; j < width; j++)
                                    {
                                        if (m_bThreadStop)
                                        {
                                            goto NEXT_LOOP;
                                        }

                                        if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                                            || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index],
                                                    byteArray[index],
                                                    byteArray[index]
                                                )
                                            );
                                        }
                                        else if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index * 2 + 1],
                                                    byteArray[index * 2 + 1],
                                                    byteArray[index * 2 + 1]
                                                )
                                            );
                                        }
                                        else if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index * 3 + 0],
                                                    byteArray[index * 3 + 1],
                                                    byteArray[index * 3 + 2]
                                                )
                                            );
                                        }

                                        index++;
                                    }
                                }
                                lockBitmap.UnlockBits();
                                SemaphoreHolder.rwLock.ExitReadLock();
                                // 委托主线程事件,虽然主线程还在is_reset.waitOne()但由于是异步委托,因此子线程会继续执行到is_reset.Release()
                                // 这样主线程就会先执行is_reset.waitOne()后的语句再完成更新UI的委托
                                RefreshUI(bmp);
                                RefreshHistogram(updateHistogram(hist));
                                RefreshCapture(bmp, is_std_state);
                                SemaphoreHolder.reset.Release();
                            }
                            else
                            {
                                Marshal.FreeCoTaskMem(buffer);
                            }
                        }

                        NEXT_LOOP:
                        ;
                    }
                }
                else if (SemaphoreHolder.is_mono)
                {
                    {
                        SemaphoreHolder.set.WaitOne();
                        SemaphoreHolder.rwLock.EnterReadLock();
                        int cameraID = m_iCameraID;
                        int width = m_iCurWidth;
                        int height = m_iCurHeight;
                        int buffersize = 0;
                        if (
                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                            || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                        )
                            buffersize = width * height;
                        if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                            buffersize = width * height * 2;
                        if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24)
                            buffersize = width * height * 3;
                        buffer = Marshal.AllocCoTaskMem(buffersize);

                        if (m_CaptureMode == CaptureMode.Video)
                        {
                            int expMs;
                            expMs = ASICameraDll2.ASIGetControlValue(
                                cameraID,
                                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE
                            );
                            ASICameraDll2.ASISetControlValue(
                                m_iCameraID,
                                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE,
                                expMs
                            );
                            expMs /= 1000;
                            ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2
                                .ASI_ERROR_CODE
                                .ASI_ERROR_INVALID_ID;
                            for (int i = 0; i < 5; i++)
                            {
                                err = ASICameraDll2.ASIGetVideoData(
                                    cameraID,
                                    buffer,
                                    buffersize,
                                    expMs * 2 + 500
                                );
                            }
                            if (err == ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
                            {
                                if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8)
                                {
                                    Mat buffer_mat = new Mat(
                                        height,
                                        width,
                                        MatType.CV_8UC1,
                                        buffer
                                    );
                                    Cv2.CalcHist(
                                        new Mat[] { buffer_mat },
                                        new int[] { 0 },
                                        new Mat(),
                                        hist,
                                        1,
                                        new int[] { 256 },
                                        new Rangef[] { new Rangef(0, 256) },
                                        uniform: true
                                    );
                                    Scalar mean_scalar,
                                        std_scalar;
                                    Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                    Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                    mean_hist = mean_scalar[0];
                                    std_hist = std_scalar[0];
                                }
                                else if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                                {
                                    Mat buffer_mat = new Mat(
                                        height,
                                        width,
                                        MatType.CV_16UC1,
                                        buffer
                                    );
                                    Cv2.CalcHist(
                                        new Mat[] { buffer_mat },
                                        new int[] { 0 },
                                        new Mat(),
                                        hist,
                                        1,
                                        new int[] { 65536 },
                                        new Rangef[] { new Rangef(0, 65536) },
                                        uniform: true
                                    );
                                    Scalar mean_scalar,
                                        std_scalar;
                                    Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                    Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                    mean_hist = mean_scalar[0];
                                    std_hist = std_scalar[0];
                                }
                                byte[] byteArray = new byte[buffersize];
                                Marshal.Copy(buffer, byteArray, 0, buffersize);

                                Marshal.FreeCoTaskMem(buffer);
                                Bitmap bmp = new Bitmap(width, height);

                                int index = 0;

                                var lockBitmap = new LockBitmap(bmp);
                                lockBitmap.LockBits();
                                for (int i = 0; i < height; i++)
                                {
                                    for (int j = 0; j < width; j++)
                                    {
                                        if (m_bThreadStop)
                                        {
                                            goto NEXT_LOOP;
                                        }

                                        if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                                            || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index],
                                                    byteArray[index],
                                                    byteArray[index]
                                                )
                                            );
                                        }
                                        else if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index * 2 + 1],
                                                    byteArray[index * 2 + 1],
                                                    byteArray[index * 2 + 1]
                                                )
                                            );
                                        }
                                        else if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index * 3 + 0],
                                                    byteArray[index * 3 + 1],
                                                    byteArray[index * 3 + 2]
                                                )
                                            );
                                        }

                                        index++;
                                    }
                                }
                                lockBitmap.UnlockBits();
                                SemaphoreHolder.rwLock.ExitReadLock();
                                // 委托主线程事件,虽然主线程还在is_reset.waitOne()但由于是异步委托,因此子线程会继续执行到is_reset.Release()
                                // 这样主线程就会先执行is_reset.waitOne()后的语句再完成更新UI的委托
                                RefreshUI(bmp);
                                RefreshHistogram(updateHistogram(hist));
                                RefreshCapture(bmp, 1);
                                SemaphoreHolder.reset.Release();
                            }
                            else
                            {
                                Marshal.FreeCoTaskMem(buffer);
                            }
                        }

                        NEXT_LOOP:
                        ;
                    }
                }
                else if (SemaphoreHolder.is_auto)
                {
                    {
                        SemaphoreHolder.set.WaitOne();
                        SemaphoreHolder.rwLock.EnterReadLock();
                        int cameraID = m_iCameraID;
                        int width = m_iCurWidth;
                        int height = m_iCurHeight;
                        int buffersize = 0;
                        if (
                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                            || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                        )
                            buffersize = width * height;
                        if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                            buffersize = width * height * 2;
                        if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24)
                            buffersize = width * height * 3;
                        buffer = Marshal.AllocCoTaskMem(buffersize);

                        if (m_CaptureMode == CaptureMode.Video)
                        {
                            int expMs;
                            expMs = ASICameraDll2.ASIGetControlValue(
                                cameraID,
                                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE
                            );
                            ASICameraDll2.ASISetControlValue(
                                m_iCameraID,
                                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE,
                                expMs
                            );
                            expMs /= 1000;
                            ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2
                                .ASI_ERROR_CODE
                                .ASI_ERROR_INVALID_ID;
                            for (int i = 0; i < 5; i++)
                            {
                                err = ASICameraDll2.ASIGetVideoData(
                                    cameraID,
                                    buffer,
                                    buffersize,
                                    expMs * 2 + 500
                                );
                            }
                            if (err == ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
                            {
                                if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8)
                                {
                                    Mat buffer_mat = new Mat(
                                        height,
                                        width,
                                        MatType.CV_8UC1,
                                        buffer
                                    );
                                    Cv2.CalcHist(
                                        new Mat[] { buffer_mat },
                                        new int[] { 0 },
                                        new Mat(),
                                        hist,
                                        1,
                                        new int[] { 256 },
                                        new Rangef[] { new Rangef(0, 256) },
                                        uniform: true
                                    );
                                    Scalar mean_scalar,
                                        std_scalar;
                                    Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                    Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                    mean_hist = mean_scalar[0];
                                    std_hist = std_scalar[0];
                                }
                                else if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                                {
                                    Mat buffer_mat = new Mat(
                                        height,
                                        width,
                                        MatType.CV_16UC1,
                                        buffer
                                    );
                                    Cv2.CalcHist(
                                        new Mat[] { buffer_mat },
                                        new int[] { 0 },
                                        new Mat(),
                                        hist,
                                        1,
                                        new int[] { 65536 },
                                        new Rangef[] { new Rangef(0, 65536) },
                                        uniform: true
                                    );
                                    Scalar mean_scalar,
                                        std_scalar;
                                    Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                    Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                    mean_hist = mean_scalar[0];
                                    std_hist = std_scalar[0];
                                }
                                byte[] byteArray = new byte[buffersize];
                                Marshal.Copy(buffer, byteArray, 0, buffersize);

                                Marshal.FreeCoTaskMem(buffer);
                                Bitmap bmp = new Bitmap(width, height);

                                int index = 0;

                                var lockBitmap = new LockBitmap(bmp);
                                lockBitmap.LockBits();
                                for (int i = 0; i < height; i++)
                                {
                                    for (int j = 0; j < width; j++)
                                    {
                                        if (m_bThreadStop)
                                        {
                                            goto NEXT_LOOP;
                                        }

                                        if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                                            || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index],
                                                    byteArray[index],
                                                    byteArray[index]
                                                )
                                            );
                                        }
                                        else if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index * 2 + 1],
                                                    byteArray[index * 2 + 1],
                                                    byteArray[index * 2 + 1]
                                                )
                                            );
                                        }
                                        else if (
                                            m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24
                                        )
                                        {
                                            lockBitmap.SetPixel(
                                                j,
                                                i,
                                                Color.FromArgb(
                                                    byteArray[index * 3 + 0],
                                                    byteArray[index * 3 + 1],
                                                    byteArray[index * 3 + 2]
                                                )
                                            );
                                        }

                                        index++;
                                    }
                                }
                                lockBitmap.UnlockBits();
                                SemaphoreHolder.rwLock.ExitReadLock();
                                // 委托主线程事件,虽然主线程还在is_reset.waitOne()但由于是异步委托,因此子线程会继续执行到is_reset.Release()
                                // 这样主线程就会先执行is_reset.waitOne()后的语句再完成更新UI的委托
                                RefreshUI(bmp);
                                RefreshHistogram(updateHistogram(hist));
                                if (SemaphoreHolder.is_mono_exp)
                                {
                                    RefreshCapture(bmp, 3);
                                }
                                else
                                {
                                    RefreshCapture(bmp, 2);
                                }

                                SemaphoreHolder.reset.Release();
                            }
                            else
                            {
                                Marshal.FreeCoTaskMem(buffer);
                            }
                        }

                        NEXT_LOOP:
                        ;
                    }
                }
                else
                {
                    SemaphoreHolder.rwLock.EnterReadLock();
                    int cameraID = m_iCameraID;
                    int width = m_iCurWidth;
                    int height = m_iCurHeight;
                    int buffersize = 0;
                    if (
                        m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                        || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                    )
                        buffersize = width * height;
                    if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                        buffersize = width * height * 2;
                    if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24)
                        buffersize = width * height * 3;

                    buffer = Marshal.AllocCoTaskMem(buffersize);

                    if (m_CaptureMode == CaptureMode.Video)
                    {
                        int expMs;
                        expMs = ASICameraDll2.ASIGetControlValue(
                            cameraID,
                            ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE
                        );
                        expMs /= 1000;
                        ASICameraDll2.ASI_ERROR_CODE err = ASICameraDll2.ASIGetVideoData(
                            cameraID,
                            buffer,
                            buffersize,
                            expMs * 2 + 500
                        );
                        if (err == ASICameraDll2.ASI_ERROR_CODE.ASI_SUCCESS)
                        {
                            if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8)
                            {
                                Mat buffer_mat = new Mat(height, width, MatType.CV_8UC1, buffer);
                                Cv2.CalcHist(
                                    new Mat[] { buffer_mat },
                                    new int[] { 0 },
                                    new Mat(),
                                    hist,
                                    1,
                                    new int[] { 256 },
                                    new Rangef[] { new Rangef(0, 256) },
                                    uniform: true
                                );
                                Scalar mean_scalar,
                                    std_scalar;
                                Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                mean_hist = mean_scalar[0];
                                std_hist = std_scalar[0];
                            }
                            else if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                            {
                                Mat buffer_mat = new Mat(height, width, MatType.CV_16UC1, buffer);
                                Cv2.CalcHist(
                                    new Mat[] { buffer_mat },
                                    new int[] { 0 },
                                    new Mat(),
                                    hist,
                                    1,
                                    new int[] { 65536 },
                                    new Rangef[] { new Rangef(0, 65536) },
                                    uniform: true
                                );
                                Scalar mean_scalar,
                                    std_scalar;
                                Cv2.MinMaxLoc(buffer_mat, out min_hist, out max_hist);
                                Cv2.MeanStdDev(buffer_mat, out mean_scalar, out std_scalar);
                                mean_hist = mean_scalar[0];
                                std_hist = std_scalar[0];
                            }
                            byte[] byteArray = new byte[buffersize];
                            Marshal.Copy(buffer, byteArray, 0, buffersize);

                            Marshal.FreeCoTaskMem(buffer);
                            Bitmap bmp = new Bitmap(width, height);

                            int index = 0;

                            var lockBitmap = new LockBitmap(bmp);
                            lockBitmap.LockBits();
                            for (int i = 0; i < height; i++)
                            {
                                for (int j = 0; j < width; j++)
                                {
                                    if (m_bThreadStop)
                                    {
                                        goto NEXT_LOOP;
                                    }

                                    if (
                                        m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8
                                        || m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8
                                    )
                                    {
                                        lockBitmap.SetPixel(
                                            j,
                                            i,
                                            Color.FromArgb(
                                                byteArray[index],
                                                byteArray[index],
                                                byteArray[index]
                                            )
                                        );
                                    }
                                    else if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16)
                                    {
                                        lockBitmap.SetPixel(
                                            j,
                                            i,
                                            Color.FromArgb(
                                                byteArray[index * 2 + 1],
                                                byteArray[index * 2 + 1],
                                                byteArray[index * 2 + 1]
                                            )
                                        );
                                    }
                                    else if (m_imgType == ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24)
                                    {
                                        lockBitmap.SetPixel(
                                            j,
                                            i,
                                            Color.FromArgb(
                                                byteArray[index * 3 + 0],
                                                byteArray[index * 3 + 1],
                                                byteArray[index * 3 + 2]
                                            )
                                        );
                                    }

                                    index++;
                                }
                            }
                            lockBitmap.UnlockBits();
                            SemaphoreHolder.rwLock.ExitReadLock();
                            // 此处必须先释放后获取,因为需要委托主线程处理事件,若不释放主线程会堵塞死
                            // 另一个作用：实时更新SemaphoreHolder.is_changed的值是否改变
                            SemaphoreHolder.rwLock.EnterReadLock();
                            // 在此处触发曝光改变事件导致写锁获得也没事,因为主线程会阻塞直到这个循环结束后才能真正写入

                            if (SemaphoreHolder.is_changed)
                            {
                                SemaphoreHolder.is_changed = false;
                                SemaphoreHolder.rwLock.ExitReadLock();
                                continue;
                            }
                            else
                            {
                                RefreshUI(bmp);
                                RefreshHistogram(updateHistogram(hist));
                                SemaphoreHolder.rwLock.ExitReadLock();
                            }
                        }
                        else
                        {
                            Marshal.FreeCoTaskMem(buffer);
                        }
                    }

                    NEXT_LOOP:
                    ;
                }
            }
        }

        public class LockBitmap
        {
            private readonly Bitmap _source = null;
            IntPtr _iptr = IntPtr.Zero;
            BitmapData _bitmapData = null;

            public byte[] Pixels { get; set; }
            public int Depth { get; private set; }
            public int Width { get; private set; }
            public int Height { get; private set; }

            public LockBitmap(Bitmap source)
            {
                this._source = source;
            }

            public Bitmap getBitmap()
            {
                return _source;
            }

            /// <summary>
            /// 锁定位图数据
            /// </summary>
            public void LockBits()
            {
                try
                {
                    // 获取位图的宽和高
                    Width = _source.Width;
                    Height = _source.Height;

                    // 获取锁定像素点的总数
                    int pixelCount = Width * Height;

                    // 创建锁定的范围
                    Rectangle rect = new Rectangle(0, 0, Width, Height);

                    // 获取像素格式大小
                    Depth = Image.GetPixelFormatSize(_source.PixelFormat);

                    // 检查像素格式
                    if (Depth != 8 && Depth != 24 && Depth != 32)
                    {
                        throw new ArgumentException("仅支持8,24和32像素位数的图像");
                    }

                    // 锁定位图并返回位图数据
                    _bitmapData = _source.LockBits(
                        rect,
                        ImageLockMode.ReadWrite,
                        _source.PixelFormat
                    );

                    // 创建字节数组以复制像素值
                    int step = Depth / 8;
                    Pixels = new byte[pixelCount * step];
                    _iptr = _bitmapData.Scan0;

                    // 将数据从指针复制到数组
                    Marshal.Copy(_iptr, Pixels, 0, Pixels.Length);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            /// <summary>
            /// 解锁位图数据
            /// </summary>
            public void UnlockBits()
            {
                try
                {
                    // 将数据从字节数组复制到指针
                    Marshal.Copy(Pixels, 0, _iptr, Pixels.Length);

                    // 解锁位图数据
                    _source.UnlockBits(_bitmapData);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            /// <summary>
            /// 获取像素点的颜色
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <returns></returns>
            public Color GetPixel(int x, int y)
            {
                Color clr = Color.Empty;

                // 获取颜色组成数量
                int cCount = Depth / 8;

                // 获取指定像素的起始索引
                int i = ((y * Width) + x) * cCount;

                if (i > Pixels.Length - cCount)
                    throw new IndexOutOfRangeException();

                if (Depth == 32) // 获得32 bpp红色，绿色，蓝色和Alpha
                {
                    byte b = Pixels[i];
                    byte g = Pixels[i + 1];
                    byte r = Pixels[i + 2];
                    byte a = Pixels[i + 3]; // a
                    clr = Color.FromArgb(a, r, g, b);
                }

                if (Depth == 24) // 获得24 bpp红色，绿色和蓝色
                {
                    byte b = Pixels[i];
                    byte g = Pixels[i + 1];
                    byte r = Pixels[i + 2];
                    clr = Color.FromArgb(r, g, b);
                }

                if (Depth == 8) // 获得8 bpp
                {
                    byte c = Pixels[i];
                    clr = Color.FromArgb(c, c, c);
                }
                return clr;
            }

            /// <summary>
            /// 设置像素点颜色
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <param name="color"></param>
            public void SetPixel(int x, int y, Color color)
            {
                // 获取颜色组成数量
                int cCount = Depth / 8;

                // 获取指定像素的起始索引
                int i = ((y * Width) + x) * cCount;

                if (Depth == 32)
                {
                    Pixels[i] = color.B;
                    Pixels[i + 1] = color.G;
                    Pixels[i + 2] = color.R;
                    Pixels[i + 3] = color.A;
                }
                if (Depth == 24)
                {
                    Pixels[i] = color.B;
                    Pixels[i + 1] = color.G;
                    Pixels[i + 2] = color.R;
                }
                if (Depth == 8)
                {
                    Pixels[i] = color.B;
                }
            }
        }

        #endregion
    }
}
