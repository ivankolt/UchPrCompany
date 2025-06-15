using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Npgsql;

namespace UchPR
{
    public partial class MaterialReceiptWindow : Window
    {
        private DataBase database;
        private ObservableCollection<MaterialReceiptItem> receiptItems;
        private string currentUserRole;
        private int? specificMaterialId;
        private string specificMaterialType;

        // Основной конструктор без параметров (существующий)
        public MaterialReceiptWindow()
        {
            InitializeComponent();
            InitializeWindow();
        }

        // Конструктор для работы с конкретным материалом (для форм списков)
        public MaterialReceiptWindow(string materialType, int materialId) : this()
        {
            specificMaterialType = materialType;
            specificMaterialId = materialId;
            LoadSpecificMaterial();
        }

        // Конструктор с указанием роли пользователя
        public MaterialReceiptWindow(string userRole) : this()
        {
            currentUserRole = userRole;
            ConfigureByUserRole();
        }

        private void InitializeWindow()
        {
            database = new DataBase();
            receiptItems = new ObservableCollection<MaterialReceiptItem>();
            dgMaterials.ItemsSource = receiptItems;

            // Генерация номера документа
            GenerateDocumentNumber();

            // Установка текущей даты
            dpDate.SelectedDate = DateTime.Now;

            // Загрузка справочников для ComboBox
            LoadMaterialTypes();
            LoadUnitsOfMeasurement();

            // Подписка на события
            SubscribeToEvents();
        }

        private void LoadSpecificMaterial()
        {
            try
            {
                if (specificMaterialId.HasValue && !string.IsNullOrEmpty(specificMaterialType))
                {
                    var item = CreateMaterialReceiptItem(specificMaterialType, specificMaterialId.Value);
                    if (item != null)
                    {
                        receiptItems.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки материала: {ex.Message}");
            }
        }

        private MaterialReceiptItem CreateMaterialReceiptItem(string materialType, int materialId)
        {
            string query = "";

            if (materialType == "fabric")
            {
                query = @"SELECT f.fabricname, f.fabricarticlenum, u.unitname 
                         FROM fabric f 
                         LEFT JOIN unitofmeasurement u ON f.accountingunitid = u.unitid 
                         WHERE f.fabricid = @materialId";
            }
            else if (materialType == "accessory")
            {
                query = @"SELECT a.accessoryname, a.accessoryarticlenum, u.unitname 
                         FROM accessory a 
                         LEFT JOIN unitofmeasurement u ON a.accountingunitid = u.unitid 
                         WHERE a.accessoryid = @materialId";
            }

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@materialId", materialId) };
            var data = database.GetData(query, parameters);

            if (data.Rows.Count > 0)
            {
                var row = data.Rows[0];
                return new MaterialReceiptItem
                {
                    MaterialType = materialType == "fabric" ? "Ткань" : "Фурнитура",
                    MaterialName = $"{row["fabricarticlenum"]} - {row["fabricname"]}",
                    MaterialId = materialId,
                    UnitName = row["unitname"].ToString(),
                    Quantity = 0,
                    UnitPrice = 0
                };
            }

            return null;
        }

        private void ConfigureByUserRole()
        {
            // Настройка интерфейса в зависимости от роли пользователя
            if (currentUserRole == "Кладовщик")
            {
                // Кладовщик имеет полный доступ
                btnAcceptDocument.IsEnabled = true;
            }
            else if (currentUserRole == "Менеджер")
            {
                // Менеджер может создавать документы, но не принимать к учету
                btnAcceptDocument.IsEnabled = false;
                btnAcceptDocument.ToolTip = "Только кладовщик может принимать документы к учету";
            }
        }

        private void GenerateDocumentNumber()
        {
            try
            {
                string query = @"SELECT COALESCE(MAX(CAST(SUBSTRING(docnumber, 4) AS INTEGER)), 0) + 1 
                                FROM receipts 
                                WHERE docnumber LIKE 'ПМ-%'";

                var result = database.GetScalarValue(query);
                int nextNumber = Convert.ToInt32(result);

                txtDocNumber.Text = $"ПМ-{nextNumber:D6}";
            }
            catch (Exception ex)
            {
                txtDocNumber.Text = $"ПМ-{DateTime.Now:yyyyMMdd}-001";
            }
        }

        private void LoadMaterialTypes()
        {
            // Настройка ComboBox для типов материалов
            var materialTypeColumn = dgMaterials.Columns[0] as DataGridComboBoxColumn;
            if (materialTypeColumn != null)
            {
                materialTypeColumn.ItemsSource = new List<string> { "Ткань", "Фурнитура" };
            }
        }

        private void LoadUnitsOfMeasurement()
        {
            try
            {
                var query = "SELECT unitname FROM unitofmeasurement ORDER BY unitname";
                var data = database.GetData(query);

                var units = new List<string>();
                foreach (DataRow row in data.Rows)
                {
                    units.Add(row["unitname"].ToString());
                }

                var unitColumn = dgMaterials.Columns[3] as DataGridComboBoxColumn;
                if (unitColumn != null)
                {
                    unitColumn.ItemsSource = units;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки единиц измерения: {ex.Message}");
            }
        }

        private void SubscribeToEvents()
        {
            btnAddLine.Click += BtnAddLine_Click;
            btnRemoveLine.Click += BtnRemoveLine_Click;
            btnSaveDocument.Click += BtnSaveDocument_Click;
            btnAcceptDocument.Click += BtnAcceptDocument_Click;

            receiptItems.CollectionChanged += (s, e) => UpdateTotalAmount();
        }

        private void BtnAddLine_Click(object sender, RoutedEventArgs e)
        {
            receiptItems.Add(new MaterialReceiptItem());
        }

        private void BtnRemoveLine_Click(object sender, RoutedEventArgs e)
        {
            if (dgMaterials.SelectedItem is MaterialReceiptItem selectedItem)
            {
                receiptItems.Remove(selectedItem);
            }
        }

        private void BtnSaveDocument_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveDocument(false);
                MessageBox.Show("Документ сохранен", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAcceptDocument_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MessageBox.Show("Принять документ к учету?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    SaveDocument(true);
                    UpdateWarehouseStocks();
                    MessageBox.Show("Документ принят к учету", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка принятия к учету: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveDocument(bool isAccepted)
        {
            // Сохранение документа поступления
            string query = @"INSERT INTO receipts (docnumber, docdate, totalamount, isaccepted) 
                            VALUES (@docNumber, @docDate, @totalAmount, @isAccepted) 
                            RETURNING receiptid";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@docNumber", txtDocNumber.Text),
                new NpgsqlParameter("@docDate", dpDate.SelectedDate ?? DateTime.Now),
                new NpgsqlParameter("@totalAmount", CalculateTotalAmount()),
                new NpgsqlParameter("@isAccepted", isAccepted)
            };

            int receiptId = Convert.ToInt32(database.GetScalarValue(query, parameters));

            // Сохранение строк документа
            foreach (var item in receiptItems.Where(i => i.Quantity > 0))
            {
                SaveReceiptLine(receiptId, item);
            }
        }

        private void SaveReceiptLine(int receiptId, MaterialReceiptItem item)
        {
            string query = @"INSERT INTO receiptlines (receiptid, materialtype, materialid, quantity, unitprice, totalsum) 
                            VALUES (@receiptId, @materialType, @materialId, @quantity, @unitPrice, @totalSum)";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@receiptId", receiptId),
                new NpgsqlParameter("@materialType", item.MaterialType == "Ткань" ? "fabric" : "accessory"),
                new NpgsqlParameter("@materialId", item.MaterialId),
                new NpgsqlParameter("@quantity", item.Quantity),
                new NpgsqlParameter("@unitPrice", item.UnitPrice),
                new NpgsqlParameter("@totalSum", item.TotalSum)
            };

            database.ExecuteQuery(query, parameters);
        }

        private void UpdateWarehouseStocks()
        {
            // Обновление складских остатков при принятии документа к учету
            foreach (var item in receiptItems.Where(i => i.Quantity > 0))
            {
                if (item.MaterialType == "Ткань")
                {
                    UpdateFabricStock(item);
                }
                else if (item.MaterialType == "Фурнитура")
                {
                    UpdateAccessoryStock(item);
                }
            }
        }

        private void UpdateFabricStock(MaterialReceiptItem item)
        {
            string query = @"INSERT INTO fabricwarehouse (fabricid, quantity, totalcost) 
                            VALUES (@materialId, @quantity, @cost)
                            ON CONFLICT (fabricid) 
                            DO UPDATE SET 
                                quantity = fabricwarehouse.quantity + @quantity,
                                totalcost = fabricwarehouse.totalcost + @cost";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@materialId", item.MaterialId),
                new NpgsqlParameter("@quantity", item.Quantity),
                new NpgsqlParameter("@cost", item.TotalSum)
            };

            database.ExecuteQuery(query, parameters);
        }

        private void UpdateAccessoryStock(MaterialReceiptItem item)
        {
            string query = @"INSERT INTO accessorywarehouse (accessoryid, quantity, totalcost) 
                            VALUES (@materialId, @quantity, @cost)
                            ON CONFLICT (accessoryid) 
                            DO UPDATE SET 
                                quantity = accessorywarehouse.quantity + @quantity,
                                totalcost = accessorywarehouse.totalcost + @cost";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@materialId", item.MaterialId),
                new NpgsqlParameter("@quantity", item.Quantity),
                new NpgsqlParameter("@cost", item.TotalSum)
            };

            database.ExecuteQuery(query, parameters);
        }

        private decimal CalculateTotalAmount()
        {
            return receiptItems.Sum(item => item.TotalSum);
        }

        private void UpdateTotalAmount()
        {
            txtTotalAmount.Text = $"{CalculateTotalAmount():C}";
        }
    }

    // Класс для элементов документа поступления
    public class MaterialReceiptItem
    {
        public string MaterialType { get; set; }
        public string MaterialName { get; set; }
        public int MaterialId { get; set; }
        public decimal Quantity { get; set; }
        public string UnitName { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalSum => Quantity * UnitPrice;
    }
}
