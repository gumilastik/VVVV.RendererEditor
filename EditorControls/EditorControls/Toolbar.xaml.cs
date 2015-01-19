using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EditorControls
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    /// 

    public partial class Toolbar : UserControl
    {
        public Toolbar()
        {
            InitializeComponent();

            Move.IsChecked = true;
            XY.IsChecked = true;
        }

        public bool IsMove()
        {
            return (Move.IsChecked == true);
        }

        public bool IsRotate()
        {
            return (Rotate.IsChecked == true);
        }

        public bool IsScale()
        {
            return (Scale.IsChecked == true);
        }

        public bool IsUniform()
        {
            return (XYU.IsChecked == true || XZU.IsChecked == true || YZU.IsChecked == true || XYZU.IsChecked == true);
        }

        public bool IsX()
        {
            return (X.IsChecked == true || XZ.IsChecked == true || XY.IsChecked == true || XYU.IsChecked == true || XZU.IsChecked == true || XYZU.IsChecked == true);
        }

        public bool IsY()
        {
            return (Y.IsChecked == true || XY.IsChecked == true || YZ.IsChecked == true || XYU.IsChecked == true || YZU.IsChecked == true || XYZU.IsChecked == true);
        }

        public bool IsZ()
        {
            return (Z.IsChecked == true || XZ.IsChecked == true || YZ.IsChecked == true || XZU.IsChecked == true || YZU.IsChecked == true || XYZU.IsChecked == true);
        }

        /*
        public delegate void WindowMoveHandler(object sender, Point args);
        public event WindowMoveHandler OnWindowMove;

        double xPos, yPos;
        bool init;
        private void UserControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            init = true;
            xPos = e.GetPosition(this).X;
            yPos = e.GetPosition(this).Y;
        }

        private void UserControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            init = false;
        }

        private void UserControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (init && e.LeftButton == MouseButtonState.Pressed)
            {
                (this.Parent as System.Windows.Forms.Control).Location = new System.Drawing.Point(100, 20);
   
              //  if(OnWindowMove != null)
              //      OnWindowMove.Invoke(this, new Point(e.GetPosition(this).X - xPos, e.GetPosition(this).Y - yPos));
            }
        }
        */

    }
}
