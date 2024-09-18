using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using ZWOptical.ASISDK;

namespace ASICamera_demo
{
    public partial class Form2 : Form
    {
        /*保存日志*/
        public static string log;

        // camera object
        private Camera m_camera = new Camera();

        // first Open
        private bool m_isFirstOpen = true;

        // last bin value
        private int m_iLastBin = 1;
        private System.Windows.Forms.Label[] ledLabels = new System.Windows.Forms.Label[16];
        System.Windows.Forms.NumericUpDown[] ledSpinBoxs = new System.Windows.Forms.NumericUpDown[
            16
        ];

        //serial
        LedArray led_array = new LedArray();

        /*串口*/
        List<byte> byteBuffer = new List<byte>(); //接收字节缓存区
        string receiveMode = "HEX模式";
        string receiveCoding = "GBK";
        string sendMode = "HEX模式";
        string sendCoding = "GBK";

        #region the callback of UI refresh delegation
        public void RefreshUI(Bitmap bmp)
        {
            if (this.InvokeRequired)
            {
                DisplayUICallback displayUI = new DisplayUICallback(DisplayUI);
                this.Invoke(displayUI, new object[] { bmp });
            }
            else
            {
                DisplayUI(bmp);
            }
        }

        public void RefreshHistogram(Bitmap bmp)
        {
            if (this.InvokeRequired)
            {
                DisplayHistogramCallback displayHistogram = new DisplayHistogramCallback(
                    DisplayHistogram
                );
                this.Invoke(displayHistogram, new object[] { bmp });
            }
            else
            {
                DisplayHistogram(bmp);
            }
        }

        public void RefreshCapture(Bitmap bmp, bool is_std_state)
        {
            if (this.InvokeRequired)
            {
                DisplayCaptureCallback displayCapture = new DisplayCaptureCallback(DisplayCapture);
                this.Invoke(displayCapture, new object[] { bmp, is_std_state });
            }
            else
            {
                DisplayCapture(bmp, is_std_state);
            }
        }

        private delegate void DisplayUICallback(Bitmap bmp);
        private delegate void DisplayHistogramCallback(Bitmap bmp);
        private delegate void DisplayCaptureCallback(Bitmap bmp, bool is_std_state);

        private void DisplayUI(Bitmap bmp)
        {
            pictureBox.Image = bmp;

            if (comboBox_captureMode.SelectedItem.ToString() == "Snap")
            {
                comboBox_captureMode.Enabled = true;
            }
            //更新设置参数
            int expMs;
            int gain;
            int width;
            int height;

            m_camera.getControlValue(ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE, out expMs);
            m_camera.getControlValue(ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN, out gain);
            width = bmp.Width;
            height = bmp.Height;
            PixelFormat format = bmp.PixelFormat;
            ASICameraDll2.ASI_IMG_TYPE img_type = m_camera.ImgType;
            string capture_config =
                "Exp Time: "
                + expMs
                + "\n"
                + "Gain: "
                + gain
                + "\n"
                + "Resolution: "
                + width
                + " * "
                + height
                + "\n"
                + "Format: "
                + Convert.ToString(format);
            capture_config_label.Text = capture_config;
        }

        private void DisplayHistogram(Bitmap bmp)
        {
            pictureBox1.Image = bmp;
            string hist_string =
                "Min: "
                + Convert.ToString(m_camera.Min_hist)
                + "\n"
                + "Max: "
                + Convert.ToString(m_camera.Max_hist)
                + "\n"
                + "Mean: "
                + Convert.ToInt32(m_camera.Mean_hist)
                + "\n"
                + "Std: "
                + Convert.ToInt32(m_camera.Std_hist)
                + "\n";
            label23.Text = hist_string;
        }

        private void DisplayCapture(Bitmap bmp, bool is_std_state)
        {
            SemaphoreHolder.refresh.WaitOne();
            //更新文件名
            m_camera.File_name = "LED" + led_array.Selected_index + "_" + led_array.Selected_value;
            string save_img_dir_path =
                m_camera.SelectedFolderPath
                + "/"
                + Camera.Datetime
                + "__"
                + m_camera.File_name
                + ".png";
            label44.Text = Convert.ToString(led_array.Selected_value);
            bmp.Save(save_img_dir_path);

            save_img_name_label.Text =
                "LED"
                + Convert.ToString(led_array.Selected_index)
                + "_"
                + Convert.ToString(led_array.Selected_value);

            log +=
                Convert.ToString(led_array.Selected_value)
                + ": ["
                + Convert.ToString(m_camera.Min_hist)
                + ","
                + Convert.ToString(m_camera.Max_hist)
                + ","
                + Convert.ToDouble(m_camera.Mean_hist)
                + ","
                + Convert.ToDouble(m_camera.Std_hist)
                + "]\n";
            SemaphoreHolder.reset.Release();
            if (
                led_array.Selected_value
                == (int)Math.Floor((100.0 / led_array.Stride)) * led_array.Stride
            )
            {
                SemaphoreHolder.log.Release();
            }
        }

        public void PopupMessageBox(string str, int iVal)
        {
            if (this.InvokeRequired)
            {
                PopMessageBoxCallback PopupMessageBox = new PopMessageBoxCallback(_PopupMessageBox);
                this.Invoke(PopupMessageBox, new object[] { str, iVal });
            }
            else
            {
                _PopupMessageBox(str, iVal);
            }
        }

        private delegate void PopMessageBoxCallback(string str, int iVal);

        private void _PopupMessageBox(string str, int iVal)
        {
            if (str == "Get Temperature")
            {
                float fTemperature = (float)iVal / 10;
                label_temperature.Text = fTemperature.ToString() + "℃";

                return;
            }

            if (str == "Gain Auto")
            {
                trackBar_gain.Value = iVal;
                spinBox_gain.Value = iVal;
                return;
            }

            if (str == "Exposure Auto")
            {
                trackBar_exposure.Value = iVal;
                spinBox_exposure.Value = iVal;
                return;
            }

            if (str == "No Camera Connection")
            {
                button_open.Enabled = false;
                label_cameraInfo.Visible = false;
                label_color.Visible = false;
                label_cooler.Visible = false;
                label_USB.Visible = false;
                label_USBHost.Visible = false;

                comboBox_cameraName.Items.Clear();
                comboBox_cameraName.Text = "";
            }

            MessageBox.Show(str);
        }

        private void Form2_Load(object sender, EventArgs e) //窗口加载事件
        {
            cbBaudRate.SelectedIndex = 1; //控件状态初始化
            cbDataBits.SelectedIndex = 3;
            cbStopBits.SelectedIndex = 0;
            cbParity.SelectedIndex = 0;
            cbReceiveMode.SelectedIndex = 0;
            cbReceiveCoding.SelectedIndex = 0;
            cbSendMode.SelectedIndex = 0;
            cbSendCoding.SelectedIndex = 0;
            btnSend.Enabled = false;
            cbPortName.Enabled = true;
            cbBaudRate.Enabled = true;
            cbDataBits.Enabled = true;
            cbStopBits.Enabled = true;
            cbParity.Enabled = true;
            /*LED Label Array*/
            ledLabels[0] = label24;
            ledLabels[1] = label25;
            ledLabels[2] = label26;
            ledLabels[3] = label27;
            ledLabels[4] = label28;
            ledLabels[5] = label29;
            ledLabels[6] = label30;
            ledLabels[7] = label31;
            ledLabels[8] = label32;
            ledLabels[9] = label33;
            ledLabels[10] = label34;
            ledLabels[11] = label35;
            ledLabels[12] = label36;
            ledLabels[13] = label37;
            ledLabels[14] = label38;
            ledLabels[15] = label39;
            /*LED SpinBox Array*/
            ledSpinBoxs[0] = numericUpDown1;
            ledSpinBoxs[1] = numericUpDown2;
            ledSpinBoxs[2] = numericUpDown3;
            ledSpinBoxs[3] = numericUpDown4;
            ledSpinBoxs[4] = numericUpDown5;
            ledSpinBoxs[5] = numericUpDown6;
            ledSpinBoxs[6] = numericUpDown7;
            ledSpinBoxs[7] = numericUpDown8;
            ledSpinBoxs[8] = numericUpDown9;
            ledSpinBoxs[9] = numericUpDown10;
            ledSpinBoxs[10] = numericUpDown11;
            ledSpinBoxs[11] = numericUpDown12;
            ledSpinBoxs[12] = numericUpDown13;
            // 此处过于粗心导致出错!!!
            //ledSpinBoxs[13] = numericUpDown14;
            //ledSpinBoxs[14] = numericUpDown14;
            ledSpinBoxs[13] = numericUpDown14;
            ledSpinBoxs[14] = numericUpDown15;
            ledSpinBoxs[15] = numericUpDown16;
            for (int i = 0; i < 16; i++)
            {
                ledLabels[i].Text = "LED" + (i + 1);
                ledSpinBoxs[i].Maximum = 100;
                ledSpinBoxs[i].Minimum = 0;
                ledSpinBoxs[i].Value = 0;
                ledSpinBoxs[i].ValueChanged += UpdateLED;
            }
            // 开启程序后默认打开相机
            button_open.PerformClick();
            // 开启后默认打开搜索串口号并打开打开最后一个找到的串口"COM6"，固定为CH340
            string[] names = System.IO.Ports.SerialPort.GetPortNames(); //搜索可用串口号并添加到下拉列表
            cbPortName.Items.Clear();
            cbPortName.Items.AddRange(names);
            cbPortName.Text = cbPortName.Items[cbPortName.Items.Count - 1].ToString();
            OpenSerialPort();
        }
        #endregion
        // Constructor
        public Form2()
        {
            InitializeComponent();
            Font newFont = new Font("微软雅黑", 8);
            this.Font = newFont;
            // Connect after opening the software
            string strCameraName = m_camera.scan();
            if (strCameraName != "")
            {
                comboBox_cameraName.Items.Add(strCameraName);
                comboBox_cameraName.SelectedIndex = 0;

                button_open.Enabled = true;
                label_cameraInfo.Visible = true;
                label_color.Visible = true;
                label_cooler.Visible = true;
                label_USB.Visible = true;
                label_USBHost.Visible = true;
                label_SN.Visible = true;
                label_temperature.Visible = true;

                if (m_camera.getIsColor())
                    label_color.Text = "Color";
                else
                    label_color.Text = "UnColor";
                if (m_camera.getIsCooler())
                    label_cooler.Text = "Cooler";
                else
                    label_cooler.Text = "UnCooler";
                if (m_camera.getIsUSB3())
                    label_USB.Text = "USB3Camera";
                else
                    label_USB.Text = "USB2Camera";
                if (m_camera.getIsUSB3Host())
                    label_USBHost.Text = "USB3Host";
                else
                    label_USBHost.Text = "USB2Host";
                if (m_camera.getIsUSB3Host())
                    label_USBHost.Text = "USB3Host";
                else
                    label_USBHost.Text = "USB2Host";
            }
            // Set the callback of UI refresh delegation
            m_camera.SetRefreshUICallBack(RefreshUI);
            m_camera.SetRefreshHistogramCallBack(RefreshHistogram);
            m_camera.SetRefreshCaptureCallBack(RefreshCapture);
            m_camera.SetMessageBoxCallBack(PopupMessageBox);
            save_path_label.Text = "Save Dir: \n ./" + Camera.Datetime;
            if (!Directory.Exists(m_camera.SelectedFolderPath))
            {
                Directory.CreateDirectory(m_camera.SelectedFolderPath);
            }
            log +=
                "Save Dir: "
                + m_camera.SelectedFolderPath
                + "\n"
                + "Date: "
                + Camera.Datetime
                + "\n"
                + "\t=======================================\t"
                + "\n";
        }

        // UI Init
        void UIInit()
        {
            // exposure time : unit us 32->10000
            int currentExpMs = m_camera.getCurrentExpMs();
            // 限制在1000000us，也就是1s
            if (currentExpMs >= 1000000)
                currentExpMs = 1000000;
            trackBar_exposure.Value = currentExpMs;
            spinBox_exposure.Value = currentExpMs;
            // gain
            int maxGain = m_camera.getMaxGain();
            trackBar_gain.Maximum = maxGain;
            spinBox_gain.Maximum = maxGain;
            int currentGain = m_camera.getCurrentGain();
            trackBar_gain.Value = currentGain;
            spinBox_gain.Value = currentGain;

            comboBox_captureMode.Items.Clear();
            comboBox_captureMode.Items.Add("Video");
            comboBox_captureMode.Items.Add("Snap");
            comboBox_captureMode.SelectedIndex = 0;

            comboBox_imageFormat.Items.Clear();
            ASICameraDll2.ASI_IMG_TYPE[] typeArr = m_camera.getImgTypeArr();
            int index = 0;
            while (typeArr[index] != ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_END)
            {
                string[] list = typeArr[index].ToString().Split('_');
                comboBox_imageFormat.Items.Add(list[2]);
                index++;
            }

            comboBox_resolution.Items.Clear();
            comboBox_bin.Items.Clear();
            int[] binArr = m_camera.getBinArr();
            index = 0;
            while (binArr[index] != 0)
            {
                comboBox_bin.Items.Add("Bin" + binArr[index].ToString());
                int width = m_camera.getMaxWidth() / binArr[index];
                int height = m_camera.getMaxHeight() / binArr[index];
                // 向下圆整
                while (width % 8 != 0)
                {
                    width--;
                }
                while (height % 2 != 0)
                {
                    height--;
                }
                comboBox_resolution.Items.Add(width.ToString() + '*' + height.ToString());
                index++;
            }
            comboBox_imageFormat.SelectedIndex = 0;
            comboBox_bin.SelectedIndex = 0;
            comboBox_resolution.SelectedIndex = 0;
            comboBox_resolution.Items.Add(Convert.ToString(1600) + '*' + Convert.ToString(900));
            comboBox_resolution.Items.Add(Convert.ToString(1280) + '*' + Convert.ToString(720));
            comboBox_resolution.Items.Add(Convert.ToString(640) + '*' + Convert.ToString(480));
            comboBox_resolution.Items.Add(Convert.ToString(320) + '*' + Convert.ToString(240));
        }

        // refresh UI Enable
        private void refreshUIEnable(bool bEnable)
        {
            comboBox_bin.Enabled = bEnable;
            comboBox_captureMode.Enabled = bEnable;
            comboBox_imageFormat.Enabled = bEnable;
            comboBox_resolution.Enabled = bEnable;

            button_close.Enabled = bEnable;
            button_scan.Enabled = !bEnable;
            button_open.Enabled = !bEnable;

            spinBox_exposure.Enabled = bEnable;
            spinBox_gain.Enabled = bEnable;
            spinBox_expLimit.Enabled = bEnable;

            trackBar_exposure.Enabled = bEnable;
            trackBar_gain.Enabled = bEnable;
            trackBar_expLimit.Enabled = bEnable;

            checkBox_gainAuto.Enabled = bEnable;
            checkBox_exposureAuto.Enabled = bEnable;
        }

        private void button_open_Click(object sender, EventArgs e)
        {
            if (m_camera.open())
            {
                refreshUIEnable(true);
                if (m_isFirstOpen)
                {
                    UIInit();
                    m_isFirstOpen = false;
                }
                if (comboBox_captureMode.SelectedItem.ToString() == "Video")
                {
                    button_startVideo.Enabled = true;
                    button_snap.Enabled = false;
                }
                else if (comboBox_captureMode.SelectedItem.ToString() == "Snap")
                {
                    button_startVideo.Enabled = false;
                    button_snap.Enabled = true;
                }

                if (comboBox_captureMode.SelectedItem.ToString() == "Video")
                    startVideo();

                // label_SN.Text = "SN: " + m_camera.getSN();

                gainAuto();
                exposureAuto();
            }
        }

        private void button_close_Click(object sender, EventArgs e)
        {
            if (m_camera.close())
            {
                refreshUIEnable(false);
                button_startVideo.Enabled = false;
                button_snap.Enabled = false;
                button_startVideo.Text = "StartVideo";
            }
        }

        private void button_startVideo_Click(object sender, EventArgs e)
        {
            startVideo();
        }

        private void startVideo()
        {
            if (button_startVideo.Text == "StartVideo")
            {
                m_camera.startCapture();
                button_startVideo.Text = "StopVideo";
                comboBox_captureMode.Enabled = false;
            }
            else if (button_startVideo.Text == "StopVideo")
            {
                m_camera.stopCapture();
                button_startVideo.Text = "StartVideo";
                comboBox_captureMode.Enabled = true;
            }
        }

        private void refreshLabel() { }

        private void button_scan_Click(object sender, EventArgs e)
        {
            string strCameraName = m_camera.scan();
            if (strCameraName != "")
            {
                comboBox_cameraName.Items.Clear();
                comboBox_cameraName.Items.Add(strCameraName);
                comboBox_cameraName.SelectedIndex = 0;

                m_isFirstOpen = true;

                button_open.Enabled = true;
                label_cameraInfo.Visible = true;
                label_color.Visible = true;
                label_cooler.Visible = true;
                label_USB.Visible = true;
                label_USBHost.Visible = true;

                if (m_camera.getIsColor())
                    label_color.Text = "Color";
                else
                    label_color.Text = "UnColor";
                if (m_camera.getIsCooler())
                    label_cooler.Text = "Cooler";
                else
                    label_cooler.Text = "UnCooler";
                if (m_camera.getIsUSB3())
                    label_USB.Text = "USB3Camera";
                else
                    label_USB.Text = "USB2Camera";
                if (m_camera.getIsUSB3Host())
                    label_USBHost.Text = "USB3Host";
                else
                    label_USBHost.Text = "USB2Host";
            }
            else
            {
                button_open.Enabled = false;
                label_cameraInfo.Visible = false;
                label_color.Visible = false;
                label_cooler.Visible = false;
                label_USB.Visible = false;
                label_USBHost.Visible = false;

                comboBox_cameraName.Items.Clear();
                comboBox_cameraName.Text = "";
            }
        }

        private void button_snap_Click(object sender, EventArgs e)
        {
            m_camera.startCapture();
            comboBox_captureMode.Enabled = false;
        }

        private void comboBox_captureMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox_captureMode.SelectedItem.ToString() == "Video")
            {
                m_camera.switchMode(Camera.CaptureMode.Video);
                button_startVideo.Enabled = true;
                button_snap.Enabled = false;
            }
            else if (comboBox_captureMode.SelectedItem.ToString() == "Snap")
            {
                m_camera.switchMode(Camera.CaptureMode.Snap);
                button_startVideo.Enabled = false;
                button_snap.Enabled = true;
            }
        }

        private void trackBar_gain_Scroll(object sender, EventArgs e)
        {
            if (!trackBar_gain.Enabled)
                return;

            int val = trackBar_gain.Value;
            if (
                m_camera.setControlValue(
                    ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN,
                    val,
                    ASICameraDll2.ASI_BOOL.ASI_FALSE
                )
            )
            {
                spinBox_gain.Value = val;
            }
        }

        private void spinBox_gain_ValueChanged(object sender, EventArgs e)
        {
            if (!spinBox_gain.Enabled)
                return;

            int val = (int)spinBox_gain.Value;
            if (
                m_camera.setControlValue(
                    ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN,
                    val,
                    ASICameraDll2.ASI_BOOL.ASI_FALSE
                )
            )
            {
                trackBar_gain.Value = val;
            }
        }

        public void getFormatParas(
            out string strType,
            out int iWidth,
            out int iHeight,
            out int iBin
        )
        {
            if (m_isFirstOpen)
            {
                strType = "RAW8";
                iWidth = m_camera.getMaxWidth();
                iHeight = m_camera.getMaxHeight();
                iBin = 1;
            }
            else
            {
                strType = comboBox_imageFormat.SelectedItem.ToString();

                string[] list = comboBox_resolution.SelectedItem.ToString().Split('*');
                iWidth = Convert.ToInt32(list[0]);
                iHeight = Convert.ToInt32(list[1]);

                iBin = (int)Char.GetNumericValue(comboBox_bin.Text.Last());
            }
            m_iLastBin = iBin;
        }

        private void comboBox_imageFormat_SelectedIndexChanged(object sender, EventArgs e)
        {
            string strType = "";
            int iBin = 0;
            int iWidth = 0;
            int iHeight = 0;
            getFormatParas(out strType, out iWidth, out iHeight, out iBin);

            m_camera.setImageFormat(iWidth, iHeight, 0, 0, iBin, str2Type(strType));
        }

        private void comboBox_bin_SelectedIndexChanged(object sender, EventArgs e)
        {
            string strType = "";
            int iBin = 0;
            int iWidth = 0;
            int iHeight = 0;

            int iLastBin = m_iLastBin;
            getFormatParas(out strType, out iWidth, out iHeight, out iBin);
            int iCurBin = m_iLastBin;
            float fRatio = (float)iLastBin / (float)iCurBin;
            float fWidth = (float)iWidth * fRatio;
            float fHeight = (float)iHeight * fRatio;
            iWidth = (int)fWidth;
            iHeight = (int)fHeight;

            // 向下圆整
            while (iWidth % 8 != 0)
            {
                iWidth--;
            }
            while (iHeight % 2 != 0)
            {
                iHeight--;
            }

            m_camera.setImageFormat(iWidth, iHeight, 0, 0, iBin, str2Type(strType));

            int index = comboBox_resolution.Items.IndexOf(
                iWidth.ToString() + '*' + iHeight.ToString()
            );
            if (index != -1)
            {
                comboBox_resolution.SelectedIndex = index;
            }
            else
            {
                string strResolution = iWidth.ToString() + '*' + iHeight.ToString();
                comboBox_resolution.Items.Add(strResolution);
                index = comboBox_resolution.Items.IndexOf(strResolution);
                comboBox_resolution.SelectedIndex = index;
            }
        }

        private void comboBox_resolution_SelectedIndexChanged(object sender, EventArgs e)
        {
            string strType = "";
            int iBin = 0;
            int iWidth = 0;
            int iHeight = 0;
            getFormatParas(out strType, out iWidth, out iHeight, out iBin);

            m_camera.setImageFormat(iWidth, iHeight, 0, 0, iBin, str2Type(strType));
        }

        // method
        public ASICameraDll2.ASI_IMG_TYPE str2Type(string strType)
        {
            if (strType == "RAW8")
            {
                return ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW8;
            }
            else if (strType == "RAW16")
            {
                return ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RAW16;
            }
            else if (strType == "RGB24")
            {
                return ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_RGB24;
            }
            else
            {
                return ASICameraDll2.ASI_IMG_TYPE.ASI_IMG_Y8;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            m_camera.close();
            m_camera.exitCaptureThread();
        }

        private void gainAuto()
        {
            int val = trackBar_gain.Value;

            if (!checkBox_gainAuto.Checked)
            {
                if (
                    m_camera.setControlValueAuto(
                        ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN,
                        val,
                        ASICameraDll2.ASI_BOOL.ASI_FALSE
                    )
                )
                {
                    trackBar_gain.Enabled = true;
                    spinBox_gain.Enabled = true;
                }
            }
            else
            {
                if (
                    m_camera.setControlValueAuto(
                        ASICameraDll2.ASI_CONTROL_TYPE.ASI_GAIN,
                        val,
                        ASICameraDll2.ASI_BOOL.ASI_TRUE
                    )
                )
                {
                    trackBar_gain.Enabled = false;
                    spinBox_gain.Enabled = false;
                }
            }
        }

        private void checkBox_gainAuto_CheckedChanged(object sender, EventArgs e)
        {
            gainAuto();
        }

        private void exposureAuto()
        {
            int val = trackBar_exposure.Value;

            if (!checkBox_exposureAuto.Checked)
            {
                if (
                    m_camera.setControlValueAuto(
                        ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE,
                        val,
                        ASICameraDll2.ASI_BOOL.ASI_FALSE
                    )
                )
                {
                    trackBar_exposure.Enabled = true;
                    spinBox_exposure.Enabled = true;
                }
            }
            else
            {
                if (
                    m_camera.setControlValueAuto(
                        ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE,
                        val,
                        ASICameraDll2.ASI_BOOL.ASI_TRUE
                    )
                )
                {
                    trackBar_exposure.Enabled = false;
                    spinBox_exposure.Enabled = false;
                }
            }
        }

        private void checkBox_ExpAuto_CheckedChanged(object sender, EventArgs e)
        {
            exposureAuto();
        }

        private void trackBar_exposure_Scroll(object sender, EventArgs e)
        {
            if (!trackBar_gain.Enabled)
                return;

            int val = trackBar_exposure.Value;
            if (
                m_camera.setControlValue(
                    ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE,
                    val,
                    ASICameraDll2.ASI_BOOL.ASI_FALSE
                )
            )
            {
                spinBox_exposure.Value = val;
            }
        }

        private void spinBox_exposure_ValueChanged(object sender, EventArgs e)
        {
            if (!spinBox_exposure.Enabled)
                return;

            int val = (int)spinBox_exposure.Value;

            SetCameraExposure(val);
            {
                trackBar_exposure.Value = val;
            }
        }

        private void SetCameraExposure(int val)
        {
            m_camera.setControlValue(
                ASICameraDll2.ASI_CONTROL_TYPE.ASI_EXPOSURE,
                val,
                ASICameraDll2.ASI_BOOL.ASI_FALSE
            );
        }

        private void trackBar_expLimit_Scroll(object sender, EventArgs e)
        {
            if (!trackBar_expLimit.Enabled)
                return;

            int val = trackBar_expLimit.Value;
            if (
                m_camera.setControlValue(
                    ASICameraDll2.ASI_CONTROL_TYPE.ASI_AUTO_MAX_EXP,
                    val,
                    ASICameraDll2.ASI_BOOL.ASI_FALSE
                )
            )
            {
                spinBox_expLimit.Value = val;
            }
        }

        private void spinBox_expLimit_ValueChanged(object sender, EventArgs e)
        {
            if (!spinBox_expLimit.Enabled)
                return;

            int val = (int)spinBox_expLimit.Value;
            if (
                m_camera.setControlValue(
                    ASICameraDll2.ASI_CONTROL_TYPE.ASI_AUTO_MAX_EXP,
                    val,
                    ASICameraDll2.ASI_BOOL.ASI_FALSE
                )
            )
            {
                trackBar_expLimit.Value = val;
            }
        }

        /**
         * 串口区
         */
        #region SerialPort
        private void UpdateLED(object sender, EventArgs e)
        {
            for (int i = 0; i < 16; i++)
            {
                led_array.Value[i] = (byte)ledSpinBoxs[i].Value;
            }
        }

        private string BytesToText(byte[] bytes, string encoding) //字节流转文本
        {
            List<byte> byteDecode = new List<byte>(); //需要转码的缓存区
            byteBuffer.AddRange(bytes); //接收字节流到接收字节缓存区
            if (encoding == "GBK")
            {
                int count = byteBuffer.Count;
                for (int i = 0; i < count; i++)
                {
                    if (byteBuffer.Count == 0)
                    {
                        break;
                    }

                    if (byteBuffer[0] < 0x80) //1字节字符
                    {
                        byteDecode.Add(byteBuffer[0]);
                        byteBuffer.RemoveAt(0);
                    }
                    else //2字节字符
                    {
                        if (byteBuffer.Count >= 2)
                        {
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                        }
                    }
                }
            }
            else if (encoding == "UTF-8")
            {
                int count = byteBuffer.Count;
                for (int i = 0; i < count; i++)
                {
                    if (byteBuffer.Count == 0)
                    {
                        break;
                    }

                    if ((byteBuffer[0] & 0x80) == 0x00) //1字节字符
                    {
                        byteDecode.Add(byteBuffer[0]);
                        byteBuffer.RemoveAt(0);
                    }
                    else if ((byteBuffer[0] & 0xE0) == 0xC0) //2字节字符
                    {
                        if (byteBuffer.Count >= 2)
                        {
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                        }
                    }
                    else if ((byteBuffer[0] & 0xF0) == 0xE0) //3字节字符
                    {
                        if (byteBuffer.Count >= 3)
                        {
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                        }
                    }
                    else if ((byteBuffer[0] & 0xF8) == 0xF0) //4字节字符
                    {
                        if (byteBuffer.Count >= 4)
                        {
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                            byteDecode.Add(byteBuffer[0]);
                            byteBuffer.RemoveAt(0);
                        }
                    }
                    else //其他
                    {
                        byteDecode.Add(byteBuffer[0]);
                        byteBuffer.RemoveAt(0);
                    }
                }
            }

            return Encoding.GetEncoding(encoding).GetString(byteDecode.ToArray());
        }

        private string BytesToHex(byte[] bytes) //字节流转HEX
        {
            string hex = "";
            foreach (byte b in bytes)
            {
                hex += b.ToString("X2") + " ";
            }

            return hex;
        }

        private byte[] TextToBytes(string str, string encoding) //文本转字节流
        {
            return Encoding.GetEncoding(encoding).GetBytes(str);
        }

        private byte[] HexToBytes(string str) //HEX转字节流
        {
            string str1 = Regex.Replace(str, "[^A-F^a-f^0-9]", ""); //清除非法字符

            double i = str1.Length; //将字符两两拆分
            int len = 2;
            string[] strList = new string[int.Parse(Math.Ceiling(i / len).ToString())];
            for (int j = 0; j < strList.Length; j++)
            {
                len = len <= str1.Length ? len : str1.Length;
                strList[j] = str1.Substring(0, len);
                str1 = str1.Substring(len, str1.Length - len);
            }

            int count = strList.Length; //将拆分后的字符依次转换为字节
            byte[] bytes = new byte[count];
            for (int j = 0; j < count; j++)
            {
                bytes[j] = byte.Parse(strList[j], NumberStyles.HexNumber);
            }

            return bytes;
        }

        private void OpenSerialPort() //打开串口
        {
            try
            {
                serialPort.PortName = cbPortName.Text;
                serialPort.BaudRate = Convert.ToInt32(cbBaudRate.Text);
                serialPort.DataBits = Convert.ToInt32(cbDataBits.Text);
                StopBits[] sb = { StopBits.One, StopBits.OnePointFive, StopBits.Two };
                serialPort.StopBits = sb[cbStopBits.SelectedIndex];
                Parity[] pt = { Parity.None, Parity.Odd, Parity.Even };
                serialPort.Parity = pt[cbParity.SelectedIndex];
                serialPort.Open();

                btnOpen.BackColor = Color.Pink;
                btnOpen.Text = "关闭串口";
                btnSend.Enabled = true;
                cbPortName.Enabled = false;
                cbBaudRate.Enabled = false;
                cbDataBits.Enabled = false;
                cbStopBits.Enabled = false;
                cbParity.Enabled = false;
            }
            catch
            {
                MessageBox.Show("串口打开失败", "提示");
            }
        }

        private void CloseSerialPort() //关闭串口
        {
            serialPort.Close();

            btnOpen.BackColor = SystemColors.ControlLight;
            btnOpen.Text = "打开串口";
            btnSend.Enabled = false;
            cbPortName.Enabled = true;
            cbBaudRate.Enabled = true;
            cbDataBits.Enabled = true;
            cbStopBits.Enabled = true;
            cbParity.Enabled = true;
        }

        private void cbPortName_DropDown(object sender, EventArgs e) //串口号下拉事件
        {
            string currentName = cbPortName.Text;
            string[] names = System.IO.Ports.SerialPort.GetPortNames(); //搜索可用串口号并添加到下拉列表
            cbPortName.Items.Clear();
            cbPortName.Items.AddRange(names);
            cbPortName.Text = currentName;
        }

        private void btnOpen_Click(object sender, EventArgs e) //打开串口点击事件
        {
            if (btnOpen.Text == "打开串口")
            {
                OpenSerialPort();
            }
            else if (btnOpen.Text == "关闭串口")
            {
                CloseSerialPort();
            }
        }

        private void btnSend_Click(object sender, EventArgs e) //发送点击事件
        {
            if (serialPort.IsOpen)
            {
                if (sendMode == "HEX模式")
                {
                    byte[] dataSend = HexToBytes(tbSend.Text); //HEX转字节流
                    int count = dataSend.Length;
                    serialPort.Write(dataSend, 0, count); //串口发送
                }
                else if (sendMode == "文本模式")
                {
                    byte[] dataSend = TextToBytes(tbSend.Text, sendCoding); //文本转字节流
                    int count = dataSend.Length;
                    serialPort.Write(dataSend, 0, count); //串口发送
                }
            }
        }

        private void btnClearReceive_Click(object sender, EventArgs e) //清空接收区点击事件
        {
            tbReceive.Clear();
        }

        private void btnClearSend_Click(object sender, EventArgs e) //清空发送区点击事件
        {
            tbSend.Clear();
        }

        private void cbReceiveMode_SelectedIndexChanged(object sender, EventArgs e) //接收模式选择事件
        {
            if (cbReceiveMode.Text == "HEX模式")
            {
                cbReceiveCoding.Enabled = false;
                receiveMode = "HEX模式";
            }
            else if (cbReceiveMode.Text == "文本模式")
            {
                cbReceiveCoding.Enabled = true;
                receiveMode = "文本模式";
            }

            byteBuffer.Clear();
        }

        private void cbReceiveCoding_SelectedIndexChanged(object sender, EventArgs e) //接收编码选择事件
        {
            if (cbReceiveCoding.Text == "GBK")
            {
                receiveCoding = "GBK";
            }
            else if (cbReceiveCoding.Text == "UTF-8")
            {
                receiveCoding = "UTF-8";
            }

            byteBuffer.Clear();
        }

        private void cbSendMode_SelectedIndexChanged(object sender, EventArgs e) //发送模式选择事件
        {
            if (cbSendMode.Text == "HEX模式")
            {
                cbSendCoding.Enabled = false;
                sendMode = "HEX模式";
            }
            else if (cbSendMode.Text == "文本模式")
            {
                cbSendCoding.Enabled = true;
                sendMode = "文本模式";
            }
        }

        private void cbSendCoding_SelectedIndexChanged(object sender, EventArgs e) //发送编码选择事件
        {
            if (cbSendCoding.Text == "GBK")
            {
                sendCoding = "GBK";
            }
            else if (cbSendCoding.Text == "UTF-8")
            {
                sendCoding = "UTF-8";
            }
        }
        #endregion SerialPort

        private void clear_button_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < 16; i++)
            {
                ledSpinBoxs[i].Value = 0;
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            led_array.Selected_index = Convert.ToByte(selected_index_box.SelectedItem);
        }

        private void stride_box_SelectedIndexChanged(object sender, EventArgs e)
        {
            led_array.Stride = Convert.ToByte(stride_box.SelectedItem);
        }

        private void serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e) //串口接收数据事件时,开启的是子进程不是主进程!
        {
            if (serialPort.IsOpen)
            {
                int count = serialPort.BytesToRead;
                byte[] dataReceive = new byte[count];
                serialPort.Read(dataReceive, 0, count); //串口接收
                this.BeginInvoke(
                    (EventHandler)(
                        delegate
                        {
                            if (receiveMode == "HEX模式")
                            {
                                tbReceive.AppendText(BytesToHex(dataReceive)); //字节流转HEX
                            }
                            else if (receiveMode == "文本模式")
                            {
                                tbReceive.AppendText(BytesToText(dataReceive, receiveCoding)); //字节流转文本
                            }
                            if (SemaphoreHolder.is_std == false && dataReceive.Contains((byte)'y'))
                            {
                                SemaphoreHolder.protect_std.Release();
                            }
                            if (SemaphoreHolder.is_std == true && dataReceive.Contains((byte)'y'))
                            {
                                SemaphoreHolder.send.Release();
                            }
                        }
                    )
                );
            }
        }
        #region cmos - led

        #endregion
        private void SendLEDValues()
        {
            if (serialPort.IsOpen)
            {
                byte[] valid_flag = { (byte)'y' };
                if (this.InvokeRequired)
                {
                    // 这样委托给主线程同时使用invoke使子线程必须等主线程write完才结束
                    // 保证了一定是先write所有再在主线程触发接收串口数据,连锁都不需要上了
                    this.BeginInvoke(
                        (MethodInvoker)
                            delegate
                            {
                                for (int j = 0; j < 16; j++)
                                {
                                    serialPort.Write(valid_flag, 0, 1);
                                    serialPort.Write(led_array.Index, j, 1);
                                    serialPort.Write(led_array.Value, j, 1);
                                }
                            }
                    );
                }
                else
                {
                    for (int j = 0; j < 16; j++)
                    {
                        serialPort.Write(valid_flag, 0, 1);
                        serialPort.Write(led_array.Index, j, 1);
                        serialPort.Write(led_array.Value, j, 1);
                    }
                }
            }
        }

        private void single_auto_button_Click(object sender, EventArgs e)
        {
            Thread thread = new Thread(() =>
            {
                single_auto_button.Invoke(
                    (MethodInvoker)
                        delegate
                        {
                            single_auto_button.Enabled = false;
                            save_img_button.Enabled = false;
                            send_button.Enabled = false;
                        }
                );

                int count = (int)Math.Floor((100.0 / led_array.Stride));
                for (int i = 0; i < 16; i++)
                {
                    led_array.Value[i] = 0;
                }

                SendLEDValues();
                SemaphoreHolder.protect_std.WaitOne();
                SemaphoreHolder.rwLock.EnterWriteLock();
                // 此处的is_std可以认为是乐观锁，允许相机一直读取数据，直到发现曝光改变那就回滚（continue）
                SemaphoreHolder.is_std = true;
                log += "Exp Time: " + 400 + "\n";
                SemaphoreHolder.rwLock.ExitWriteLock();

                for (int i = 0; i <= count; i++)
                {
                    SemaphoreHolder.reset.WaitOne();
                    SemaphoreHolder.reset.WaitOne();
                    led_array.Selected_value = (byte)(led_array.Stride * i);
                    led_array.Value[led_array.Selected_index] = led_array.Selected_value;
                    SendLEDValues();
                    // 由于SendLEDValues的大部分操作在主线程完成
                    // 此时已经完成主线程 “发送完->再接收”的同步，因此“生产者-消费者问题”只需要一个信号量
                    SemaphoreHolder.send.WaitOne();
                    int val = 500 * i;
                    // SetCameraExposure()内调用的函数里已经有写锁保护,不需要再加
                    SetCameraExposure(val);
                    SemaphoreHolder.set.Release();
                    SemaphoreHolder.refresh.Release();
                }

                SemaphoreHolder.rwLock.EnterWriteLock();
                SemaphoreHolder.is_std = false;
                SemaphoreHolder.rwLock.ExitWriteLock();
                SemaphoreHolder.log.WaitOne();
                try
                {
                    // 直接将字符串写入文件，如果文件已存在则覆盖
                    File.WriteAllText(
                        m_camera.SelectedFolderPath + "/" + Camera.Datetime + ".txt",
                        log
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine("保存文件时出错: " + ex.Message);
                }
                single_auto_button.Invoke(
                    (MethodInvoker)
                        delegate
                        {
                            single_auto_button.Enabled = true;
                            save_img_button.Enabled = true;
                            send_button.Enabled = true;
                        }
                );
            });
            thread.Start();
        }

        private void send_button_Click(object sender, EventArgs e)
        {
            SendLEDValues();
        }

        private void open_dir_button_Click(object sender, EventArgs e)
        {
            Process.Start(m_camera.SelectedFolderPath);
        }

        private void save_img_button_Click(object sender, EventArgs e)
        {
            this.Invoke(
                (MethodInvoker)
                    delegate
                    {
                        save_img_name_label.Text =
                            "LED"
                            + Convert.ToString(led_array.Selected_index)
                            + "_"
                            + Convert.ToString(led_array.Selected_value);
                    }
            );
        }
    }
}
