using System.Windows;
using System.Windows.Controls;

namespace UchPR
{
    public partial class MainWindow : Window
    {
        // При создании главного окна мы передаем сюда роль вошедшего пользователя
        public MainWindow(string userRole, string userName)
        {
            InitializeComponent();

            UserInfo.Text = $"Пользователь: {userName}, Роль: {userRole}";

            // Передаем роль на страницу, чтобы управлять видимостью кнопок
            Page initialPage = null;

            switch (userRole)
            {
                case "Кладовщик":
                    // Основная страница для кладовщика - это склад
                    initialPage = new WarehousePage(userRole);
                    break;

                case "Менеджер":
                case "Руководитель":
                    // Их основная страница - список изделий. Но пока ее нет,
                    // для теста можем направить их тоже на склад.
                    // В будущем здесь будет new ProductsPage(userRole);
                    initialPage = new WarehousePage(userRole);
                    break;

                case "Заказчик":
                    // initialPage = new CustomerOrderPage(userRole);
                    break;
            }

            if (initialPage != null)
            {
                MainFrame.Navigate(initialPage);
            }
        }
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            LoginWindow loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();
        }
    }
}
