using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace UchPR
{
    public partial class ProductListPage : Page
    {
        private DataBase database;
        private DataTable productsTable;
        private string currentUserRole;
        private bool isManager;
        private bool isDirector;

        public ProductListPage(string userRole)
        {
            InitializeComponent();
            database = new DataBase();
            currentUserRole = userRole;

            // Проверка прав доступа
            isManager = (currentUserRole == "Менеджер");
            isDirector = (currentUserRole == "Руководитель");

            if (!isManager && !isDirector)
            {
                MessageBox.Show("У вас нет прав для доступа к этой форме", "Ошибка доступа",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                if (NavigationService != null && NavigationService.CanGoBack)
                {
                    NavigationService.GoBack();
                };
                return;
            }

            ConfigureAccessByRole();
            cmbCategory.SelectedIndex = 0;
            LoadProducts();
        }

        private void ConfigureAccessByRole()
        {
            // Настройка видимости столбцов в зависимости от роли
            if (isManager)
            {
                // Менеджер не видит финансовые данные
                dgProducts.Columns[7].Visibility = Visibility.Collapsed; // Себестоимость
                dgProducts.Columns[9].Visibility = Visibility.Collapsed; // Маржа

                // Менеджер не может добавлять изделия, только планировать производство
                btnAdd.Visibility = Visibility.Collapsed;
            }

            if (isDirector)
            {
                // Руководитель видит все данные
                btnReport.Content = "Финансовый отчет";
            }
        }

        private void LoadProducts()
        {
            try
            {
                string query = @"
                    SELECT 
                        p.productid,
                        p.productarticlenum,
                        p.productname,
                        p.productcategory,
                        p.productdescription,
                        p.productiontime,
                        COALESCE(p.costprice, 0) as CostPrice,
                        COALESCE(p.saleprice, 0) as SalePrice
                    FROM product p
                    ORDER BY p.productarticlenum";

                productsTable = database.GetData(query);

                // Добавляем вычисляемые поля
                AddCalculatedColumns();
                ProcessProductData();

                dgProducts.ItemsSource = productsTable.DefaultView;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных об изделиях: {ex.Message}");
            }
        }

        private void AddCalculatedColumns()
        {
            if (!productsTable.Columns.Contains("ProductionStatus"))
                productsTable.Columns.Add("ProductionStatus", typeof(string));

            if (!productsTable.Columns.Contains("AvailableQuantity"))
                productsTable.Columns.Add("AvailableQuantity", typeof(int));

            if (!productsTable.Columns.Contains("MarginPercent"))
                productsTable.Columns.Add("MarginPercent", typeof(decimal));

            if (!productsTable.Columns.Contains("AvailabilityColor"))
                productsTable.Columns.Add("AvailabilityColor", typeof(string));
        }

        private void ProcessProductData()
        {
            foreach (DataRow row in productsTable.Rows)
            {
                int productId = Convert.ToInt32(row["productid"]);
                decimal costPrice = Convert.ToDecimal(row["CostPrice"]);
                decimal salePrice = Convert.ToDecimal(row["SalePrice"]);

                // Расчет маржи
                if (costPrice > 0 && salePrice > costPrice)
                {
                    row["MarginPercent"] = Math.Round(((salePrice - costPrice) / costPrice) * 100, 2);
                }
                else
                {
                    row["MarginPercent"] = 0;
                }

                // Проверка доступности материалов для производства
                var availability = CheckMaterialAvailability(productId);
                row["ProductionStatus"] = availability.Status;
                row["AvailableQuantity"] = availability.AvailableQuantity;
                row["AvailabilityColor"] = availability.Color;
            }
        }

        private MaterialAvailability CheckMaterialAvailability(int productId)
        {
            try
            {
                // Получаем состав изделия
                string compositionQuery = @"
                    SELECT 
                        fp.fabricid, fp.requiredquantity as FabricRequired,
                        ap.accessoryid, ap.requiredquantity as AccessoryRequired
                    FROM product p
                    LEFT JOIN fabricproducts fp ON p.productid = fp.productid
                    LEFT JOIN accessoryproducts ap ON p.productid = ap.productid
                    WHERE p.productid = @productId";

                var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@productId", productId) };
                var compositionData = database.GetData(compositionQuery, parameters);

                int minAvailableQuantity = int.MaxValue;
                bool allMaterialsAvailable = true;

                foreach (DataRow row in compositionData.Rows)
                {
                    // Проверяем ткани
                    if (!row.IsNull("fabricid"))
                    {
                        int fabricId = Convert.ToInt32(row["fabricid"]);
                        decimal required = Convert.ToDecimal(row["FabricRequired"]);
                        decimal available = GetFabricStock(fabricId);

                        if (available < required)
                        {
                            allMaterialsAvailable = false;
                            minAvailableQuantity = 0;
                        }
                        else
                        {
                            int possibleQuantity = (int)(available / required);
                            minAvailableQuantity = Math.Min(minAvailableQuantity, possibleQuantity);
                        }
                    }

                    // Проверяем фурнитуру
                    if (!row.IsNull("accessoryid"))
                    {
                        int accessoryId = Convert.ToInt32(row["accessoryid"]);
                        decimal required = Convert.ToDecimal(row["AccessoryRequired"]);
                        decimal available = GetAccessoryStock(accessoryId);

                        if (available < required)
                        {
                            allMaterialsAvailable = false;
                            minAvailableQuantity = 0;
                        }
                        else
                        {
                            int possibleQuantity = (int)(available / required);
                            minAvailableQuantity = Math.Min(minAvailableQuantity, possibleQuantity);
                        }
                    }
                }

                if (minAvailableQuantity == int.MaxValue)
                    minAvailableQuantity = 0;

                return new MaterialAvailability
                {
                    Status = allMaterialsAvailable ? "Доступно" : "Недостаток материалов",
                    AvailableQuantity = minAvailableQuantity,
                    Color = allMaterialsAvailable ? "#00FF00" : "#FF0000"
                };
            }
            catch (Exception ex)
            {
                return new MaterialAvailability
                {
                    Status = "Ошибка проверки",
                    AvailableQuantity = 0,
                    Color = "#FF0000"
                };
            }
        }

        private decimal GetFabricStock(int fabricId)
        {
            try
            {
                string query = "SELECT COALESCE(quantity, 0) FROM fabricwarehouse WHERE fabricid = @fabricId";
                var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@fabricId", fabricId) };
                var result = database.GetData(query, parameters);

                if (result.Rows.Count > 0)
                    return Convert.ToDecimal(result.Rows[0][0]);

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private decimal GetAccessoryStock(int accessoryId)
        {
            try
            {
                string query = "SELECT COALESCE(quantity, 0) FROM accessorywarehouse WHERE accessoryid = @accessoryId";
                var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@accessoryId", accessoryId) };
                var result = database.GetData(query, parameters);

                if (result.Rows.Count > 0)
                    return Convert.ToDecimal(result.Rows[0][0]);

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ChkAvailable_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (productsTable == null) return;

            string filter = "";
            var conditions = new List<string>();

            // Фильтр по поиску
            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                conditions.Add($"(productarticlenum LIKE '%{txtSearch.Text}%' OR productname LIKE '%{txtSearch.Text}%')");
            }

            // Фильтр по категории
            if (cmbCategory.SelectedIndex > 0)
            {
                var selectedCategory = ((ComboBoxItem)cmbCategory.SelectedItem).Content.ToString();
                conditions.Add($"productcategory = '{selectedCategory}'");
            }

            // Фильтр по доступности
            if (chkAvailable.IsChecked == true)
            {
                conditions.Add("AvailableQuantity > 0");
            }

            if (conditions.Count > 0)
            {
                filter = string.Join(" AND ", conditions);
            }

            productsTable.DefaultView.RowFilter = filter;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadProducts();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!isDirector)
            {
                MessageBox.Show("Только руководитель может добавлять новые изделия");
                return;
            }

        //    var addWindow = new ProductEditWindow();
          //  if (addWindow.ShowDialog() == true)
            {
                LoadProducts();
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (dgProducts.SelectedItem == null)
            {
                MessageBox.Show("Выберите изделие для редактирования");
                return;
            }

            var selectedRow = ((DataRowView)dgProducts.SelectedItem).Row;
            int productId = Convert.ToInt32(selectedRow["productid"]);

          //  var editWindow = new ProductEditWindow(productId, currentUserRole);
          //  if (editWindow.ShowDialog() == true)
          //  {
                LoadProducts();
           // }
        }

        private void BtnComposition_Click(object sender, RoutedEventArgs e)
        {
            if (dgProducts.SelectedItem == null)
            {
                MessageBox.Show("Выберите изделие для просмотра состава");
                return;
            }

            var selectedRow = ((DataRowView)dgProducts.SelectedItem).Row;
            int productId = Convert.ToInt32(selectedRow["productid"]);
            string productName = selectedRow["productname"].ToString();

          //  var compositionWindow = new ProductCompositionWindow(productId, productName, currentUserRole);
          //  compositionWindow.ShowDialog();
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
          //  var calculateWindow = new MaterialRequirementWindow(currentUserRole);
           // calculateWindow.ShowDialog();
        }

        private void BtnReport_Click(object sender, RoutedEventArgs e)
        {
            if (isDirector)
            {
               // var reportWindow = new ProductFinancialReportWindow();
               // reportWindow.ShowDialog();
            }
            else
            {
               // var reportWindow = new ProductReportWindow();
               // reportWindow.ShowDialog();
            }
        }
    }

    public class MaterialAvailability
    {
        public string Status { get; set; }
        public int AvailableQuantity { get; set; }
        public string Color { get; set; }
    }
}
