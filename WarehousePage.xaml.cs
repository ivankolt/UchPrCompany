// File: WarehousePage.xaml.cs
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace UchPR
{
    public partial class WarehousePage : Page
    {
        private readonly DataBase db = new DataBase();
        private List<UnitOfMeasurement> allUnits; // Кэшируем список, чтобы не дергать БД каждый раз
        private readonly string _userRole;

        public WarehousePage(string userRole)
        {
            InitializeComponent();
            _userRole = userRole;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Загружаем все единицы измерения один раз при загрузке страницы
            allUnits = db.GetAllUnits();
            SetPermissions(); 
            // По умолчанию выбираем "Ткани" и загружаем данные
            cmbMaterialType.SelectedIndex = 0;
        }

        private void SetPermissions()
        {
            if (_userRole == "Кладовщик")
            {
                btnSetThreshold.Visibility = Visibility.Visible;
            }
            else
            {
                btnSetThreshold.Visibility = Visibility.Collapsed;
            }
            // Здесь можно будет добавить другие правила, например, для кнопок "Добавить поступление"
        }

        private void btnSetThreshold_Click(object sender, RoutedEventArgs e)
        {
            string materialType = cmbMaterialType.SelectedIndex == 0 ? "Fabric" : "Accessory";
            var thresholdWindow = new ThresholdSettingsWindow(materialType);
            thresholdWindow.ShowDialog();

            // После закрытия окна обновляем данные
            LoadStockData(materialType);
        }

        private void btnScrapLog_Click(object sender, RoutedEventArgs e)
        {
            var scrapLogWindow = new ScrapLogWindow();
            scrapLogWindow.ShowDialog();
        }

        private void cmbMaterialType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Проверяем, чтобы страница была уже загружена, чтобы избежать ошибок при инициализации
            if (!IsLoaded) return;

            if (cmbMaterialType.SelectedIndex == 0) // Выбраны "Ткани"
            {
                LoadStockData("Fabric");
            }
            else // Выбрана "Фурнитура"
            {
                LoadStockData("Accessory");
            }
        }

        /// <summary>
        /// Загружает данные со склада в таблицу в зависимости от типа материала.
        /// </summary>
        private void LoadStockData(string type)
        {
            List<MaterialStockItem> stockData;
            if (type == "Fabric")
            {
                stockData = db.GetFabricStock();
            }
            else
            {
                stockData = db.GetAccessoryStock();
            }

            // Подготавливаем каждую строку для отображения
            foreach (var item in stockData)
            {
                item.DisplayQuantity = item.BaseQuantity; // Начальное отображаемое количество
                item.AvailableUnits = allUnits; // Присваиваем полный список единиц
                // Устанавливаем выбранную единицу по умолчанию (базовую)
                item.SelectedUnit = allUnits.FirstOrDefault(u => u.Code == item.BaseUnitId);
            }

            dgMaterials.ItemsSource = stockData;
        }
        
        /// <summary>
        /// Вызывается при смене единицы измерения ВНУТРИ строки DataGrid
        /// </summary>
        private void UnitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Игнорируем событие, если оно вызвано не реальным выбором пользователя
            if (e.AddedItems.Count == 0) return;

            var comboBox = sender as ComboBox;
            // Получаем объект MaterialStockItem, к которому привязан этот ComboBox
            var selectedMaterial = comboBox?.DataContext as MaterialStockItem;

            if (selectedMaterial != null && selectedMaterial.SelectedUnit != null)
            {
                // Получаем коэффициент пересчета из базовой единицы в выбранную
                decimal factor = db.GetConversionFactor(
                    selectedMaterial.Article,
                    selectedMaterial.BaseUnitId,
                    selectedMaterial.SelectedUnit.Code
                );

                // Пересчитываем отображаемое количество
                selectedMaterial.DisplayQuantity = selectedMaterial.BaseQuantity * factor;

                // Обновляем всю таблицу, чтобы изменения отразились.
                // Это самый простой способ обновить значение в ячейке.
                dgMaterials.Items.Refresh();
            }
        }
    }
}
