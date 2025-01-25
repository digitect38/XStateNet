using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace XSView
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Rectangle _rect;    // 드래그할 사각형

        public MainWindow()
        {
            InitializeComponent();
            new StateShape().DrawOnCanvas(this, 100, 100, Brushes.Bisque);
            new StateShape().DrawOnCanvas(this, 200, 100, Brushes.LightBlue);
            new StateShape().DrawOnCanvas(this, 100, 200, Brushes.LightSalmon);
            new StateShape().DrawOnCanvas(this, 200, 200, Brushes.LightSlateGray);
        }       
    }

    public abstract class  Shape
    {
        protected bool _isDragging;   // 드래그 중인지 여부
        protected Point _mouseOffset; // 마우스와 사각형 왼쪽 상단 간의 오프셋

        public abstract void DrawOnCanvas(Window canvas, int x, int y, Brush brush);
    }

    public class StateShape : Shape
    {
        int _x_radius = 50;
        int _y_radius = 50;
        Window _win;
        Canvas _canvas;
        Rectangle _rect;

        public StateShape()
        {
        }

        public override void DrawOnCanvas(Window win, int x, int y, Brush brush) {

            _win = win;

            _rect = new Rectangle() 
            { 
                Width = 10 * _x_radius, 
                Height = 8 * _y_radius, 
                RadiusX = _x_radius, 
                RadiusY = _y_radius,
                Fill = brush,
                Stroke = Brushes.Black,
                StrokeThickness = 5
            };

            Canvas.SetLeft(_rect, x);
            Canvas.SetTop(_rect, y);
                        
            _canvas = (Canvas)win.FindName("XStateNetCanvas");  // get canvas from _win
            _canvas.Children.Add(_rect);

            _rect.MouseLeftButtonDown += Rect_MouseLeftButtonDown;
            _rect.MouseMove += Rect_MouseMove;
            _rect.MouseLeftButtonUp += Rect_MouseLeftButtonUp;
        }

        private void Rect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _rect.CaptureMouse(); // 마우스 캡처(드래그 중 이동시에도 이벤트 수신 가능)

            // (사각형의 Canvas 좌표) - (마우스 좌표) 차이를 저장해 놓으면
            // 드래그 중에 사각형을 원하는 위치로 정확히 이동하기 편리합니다.
            Point mousePos = e.GetPosition(_canvas);
            double left = Canvas.GetLeft(_rect);
            double top = Canvas.GetTop(_rect);

            _mouseOffset = new Point(mousePos.X - left, mousePos.Y - top);

            // 이벤트 미처리 시 다른 요소로 마우스 이벤트가 전파될 수 있으므로 처리 완료

            // make top most _selectedRectangle 
            _canvas.Children.Remove(_rect);
            _canvas.Children.Add(_rect);

            e.Handled = true;
        }

        private void Rect_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                // 마우스 현재 위치
                Point mousePos = e.GetPosition(_canvas);

                // 오프셋을 고려해 사각형 위치 지정
                double newLeft = mousePos.X - _mouseOffset.X;
                double newTop = mousePos.Y - _mouseOffset.Y;

                Canvas.SetLeft(_rect, newLeft);
                Canvas.SetTop(_rect, newTop);
            }
            e.Handled = true;
        }

        private void Rect_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _rect.ReleaseMouseCapture(); // 마우스 캡처 해제
            e.Handled = true;
        }
    }
}