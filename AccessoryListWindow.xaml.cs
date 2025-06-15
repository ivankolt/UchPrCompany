using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace UchPR
{
    public partial class AccessoryListWindow : Window
    {
        private DataBase database;
        private DataTable accessoriesTable;
        private string currentUserRole;
        private List<UnitOfMeasurement> units;
        private UnitOfMeasurement selectedUnit;

        public AccessoryListWindow(string userRole)
        {
            InitializeComponent();
            database = new DataBase();
            currentUserRole = userRole;

            // Проверка прав доступа
            if (currentUserRole != "Кладовщик")
            {
                MessageBox.Show("У вас нет прав для доступа к этой форме", "Ошибка доступа",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                this.Close();
                return;
            }

            LoadUnits();
            cmbType.SelectedIndex = 0;
            LoadAccessories();
        }

        private void LoadUnits()
        {
            try
            {
                var query = "SELECT * FROM unitofmeasurement ORDER BY unitname";
                var unitsData = database.GetData(query);

                units = new List<UnitOfMeasurement>();
                cmbUnit.Items.Add("Все единицы");

                foreach (DataRow row in unitsData.Rows)
                {
                    var unit = new UnitOfMeasurement
                    {
                        Code = Convert.ToInt32(row["unitid"]),
                        Name = row["unitname"].ToString(),
                        ConversionFactor = Convert.ToDecimal(row["conversionfactor"])
                    };
                    units.Add(unit);
                    cmbUnit.Items.Add(unit.Name);
                }

                cmbUnit.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки единиц измерения: {ex.Message}");
            }
        }

        private void LoadAccessories()
        {
            try
            {
                string query = @"
                    SELECT 
                        a.accessoryid,
                        a.accessoryarticlenum,
                        a.accessoryname,
                        a.accessorytype,
                        c.colorname as ColorName,
                        COALESCE(aw.quantity, 0) as StockQuantity,
                        COALESCE(aw.totalcost, 0) as TotalCost,
                        CASE 
                            WHEN COALESCE(aw.quantity, 0) > 0 
                            THEN COALESCE(aw.totalcost, 0) / aw.quantity 
                            ELSE 0 
                        END as AveragePrice,
                        u.unitname as AccountingUnitName,
                        COALESCE(a.minstock, 0) as MinStock,
                        COALESCE(a.scraplimit, 0) as ScrapLimit
                    FROM accessory a
                    LEFT JOIN accessorywarehouse aw ON a.accessoryid = aw.accessoryid
                    LEFT JOIN colors c ON a.accessorycolorid = c.colorid
                    LEFT JOIN unitofmeasurement u ON a.accountingunitid = u.unitid
                    ORDER BY a.accessoryarticlenum";

                accessoriesTable = database.GetData(query);

                // Добавляем вычисляемые поля
                AddCalculatedColumns();
                ProcessAccessoryData();

                dgAccessories.ItemsSource = accessoriesTable.DefaultView;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных о фурнитуре: {ex.Message}");
            }
        }

        private void AddCalculatedColumns()
        {
            if (!accessoriesTable.Columns.Contains("ConvertedQuantity"))
                accessoriesTable.Columns.Add("ConvertedQuantity", typeof(decimal));

            if (!accessoriesTable.Columns.Contains("SelectedUnitName"))
                accessoriesTable.Columns.Add("SelectedUnitName", typeof(string));

            if (!accessoriesTable.Columns.Contains("StatusColor"))
                accessoriesTable.Columns.Add("StatusColor", typeof(string));
        }

        private void ProcessAccessoryData()
        {
            foreach (DataRow row in accessoriesTable.Rows)
            {
                decimal stockQuantity = Convert.ToDecimal(row["StockQuantity"]);
                decimal minStock = Convert.ToDecimal(row["MinStock"]);
                decimal scrapLimit = Convert.ToDecimal(row["ScrapLimit"]);

                // Конвертация количества в выбранную единицу
                if (selectedUnit != null)
                {
                    row["ConvertedQuantity"] = stockQuantity * selectedUnit.ConversionFactor;
                    row["SelectedUnitName"] = selectedUnit.Name;
                }
                else
                {
                    row["ConvertedQuantity"] = stockQuantity;
                    row["SelectedUnitName"] = row["AccountingUnitName"];
                }

                // Определение статуса остатка
                if (stockQuantity <= scrapLimit)
                    row["StatusColor"] = "#FF0000"; // Красный - обрезки
                else if (stockQuantity <= minStock)
                    row["StatusColor"] = "#FFA500"; // Оранжевый - критический остаток
                else
                    row["StatusColor"] = "#00FF00"; // Зеленый - нормальный остаток
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CmbUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbUnit.SelectedIndex > 0)
            {
                selectedUnit = units[cmbUnit.SelectedIndex - 1];
            }
            else
            {
                selectedUnit = null;
            }

            ProcessAccessoryData();
            ApplyFilters();
        }

        private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (accessoriesTable == null) return;

            string filter = "";
            var conditions = new List<string>();

            // Фильтр по поиску
            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                conditions.Add($"(accessoryarticlenum LIKE '%{txtSearch.Text}%' OR accessoryname LIKE '%{txtSearch.Text}%')");
            }

            // Фильтр по типу
            if (cmbType.SelectedIndex > 0)
            {
                var selectedType = ((ComboBoxItem)cmbType.SelectedItem).Content.ToString();
                conditions.Add($"accessorytype = '{selectedType}'");
            }

            if (conditions.Count > 0)
            {
                filter = string.Join(" AND ", conditions);
            }

            accessoriesTable.DefaultView.RowFilter = filter;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadAccessories();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
          //  var addWindow = new AccessoryEditWindow();
           // if (addWindow.ShowDialog() == true)
            {
                LoadAccessories();
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (dgAccessories.SelectedItem == null)
            {
                MessageBox.Show("Выберите фурнитуру для редактирования");
                return;
            }

            var selectedRow = ((DataRowView)dgAccessories.SelectedItem).Row;
            int accessoryId = Convert.ToInt32(selectedRow["accessoryid"]);

          //  var editWindow = new AccessoryEditWindow(accessoryId);
          //  if (editWindow.ShowDialog() == true)
            {
                LoadAccessories();
            }
        }

        private void BtnReceive_Click(object sender, RoutedEventArgs e)
        {
            if (dgAccessories.SelectedItem == null)
            {
                MessageBox.Show("Выберите фурнитуру для оформления поступления");
                return;
            }

            var selectedRow = ((DataRowView)dgAccessories.SelectedItem).Row;
            int accessoryId = Convert.ToInt32(selectedRow["accessoryid"]);

            var receiptWindow = new MaterialReceiptWindow("accessory", accessoryId);
            if (receiptWindow.ShowDialog() == true)
            {
                LoadAccessories();
            }
        }

        private void BtnScrap_Click(object sender, RoutedEventArgs e)
        {
            ProcessScrapAccessories();
        }

        private void ProcessScrapAccessories()
        {
            try
            {
                var scrapItems = new List<string>();
                decimal totalScrapCost = 0;

                foreach (DataRow row in accessoriesTable.Rows)
                {
                    decimal stockQuantity = Convert.ToDecimal(row["StockQuantity"]);
                    decimal scrapLimit = Convert.ToDecimal(row["ScrapLimit"]);

                    if (stockQuantity > 0 && stockQuantity <= scrapLimit)
                    {
                        decimal itemCost = Convert.ToDecimal(row["TotalCost"]);
                        scrapItems.Add($"{row["accessoryname"]}: {stockQuantity} {row["AccountingUnitName"]} на сумму {itemCost:F2} руб.");
                        totalScrapCost += itemCost;

                        // Списываем обрезки
                        ScrapAccessory(Convert.ToInt32(row["accessoryid"]), stockQuantity, itemCost);
                    }
                }

                if (scrapItems.Count > 0)
                {
                    string message = $"Списано обрезков на общую сумму {totalScrapCost:F2} руб.:\n\n";
                    message += string.Join("\n", scrapItems);
                    MessageBox.Show(message, "Списание обрезков");
                    LoadAccessories();
                }
                else
                {
                    MessageBox.Show("Нет фурнитуры для списания в обрезки");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при списании обрезков: {ex.Message}");
            }
        }

        private void ScrapAccessory(int accessoryId, decimal quantity, decimal cost)
        {
            string query = @"
                INSERT INTO scraplog (materialtype, materialid, quantity, cost, scrapdate, reason)
                VALUES ('accessory', @accessoryId, @quantity, @cost, @scrapDate, 'Автоматическое списание обрезков');
                
                UPDATE accessorywarehouse 
                SET quantity = 0, totalcost = 0 
                WHERE accessoryid = @accessoryId";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@accessoryId", accessoryId),
                new NpgsqlParameter("@quantity", quantity),
                new NpgsqlParameter("@cost", cost),
                new NpgsqlParameter("@scrapDate", DateTime.Now)
            };

            database.ExecuteQuery(query, parameters);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
           // var settingsWindow = new AccessorySettingsWindow();
          //  if (settingsWindow.ShowDialog() == true)
          //  {
                LoadAccessories();
          //  }
        }
    }
}
