
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using System.Text;
using application1.Models.Function;
using static application1.Models.Products.Product;
using application1.Models.Products;
using static application1.Models.Products.TransactionHistory;
using application1.ViewModel;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace application1.Controllers
{//done
    public class PaymentController : Base1Controller
    {
        public PaymentController(AppdbContext context) : base(context) { }

        public IActionResult paymentcheckout(string couponCode)
        {
            var GetCustomerLogin = GetLoginAdmin().Result;
            if (GetCustomerLogin == null)
                return RedirectToAction("login", "custermor");
            var appliedCoupon = db.Coupon.FirstOrDefault(c => c.Code == couponCode);
            if (appliedCoupon == null)
            {
                ViewBag.ErrorMsg = "Invalid coupon code.";
                return RedirectToAction("cartpage", "custermor");
            }
            var result = db.CartList.Include(x => x.Product).Where(x => x.Quantity > x.Product.ProductStock && x.CustomerId == GetCustomerLogin.Id).ToList();

            if (result.Any())
            {
                ViewBag.ErrorMsg = "Not enough stock available for some products.";
                return RedirectToAction("cartpage", "custermor");
            }
            string domain = "http://louise99-001-site1.otempurl.com/";
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string>()
                {
                    "card",
                },
                LineItems = new List<SessionLineItemOptions>(),
                Mode = "payment",
                //SuccessUrl = "http://localhost:5129/Payment/paymentsucersspage",
                //CancelUrl = "http://localhost:5129/Payment/paymenterrorpage"
                SuccessUrl = domain + "Payment/paymentsucersspage",
                CancelUrl = domain + "Payment/paymenterrorpage"

            };
            var id = Convert.ToInt32(HttpContext.Session.GetString("customerid"));
            //var logiunser = GetLoginAdmin().Result;
            if (GetCustomerLogin == null)
            {
                return RedirectToAction("login", "custermor");

            }
            var discount = db.Coupon.Where(x => x.Code == couponCode).FirstOrDefault();
            var discountValue = 0m;

            // 处理折扣类型
            if (discount.Type == application1.Models.Products.Coupon.DiscountType.FixedAmount)
            {
                discountValue = discount.DiscountValue; // 固定金额折扣
            }
            else if (discount.Type == application1.Models.Products.Coupon.DiscountType.Percentage)
            {
                discountValue = discount.DiscountValue / 100; // 百分比折扣，转为小数
            }

            // 获取购物车中的商品列表
            var cartlist = db.CartList.Include(e => e.Product)
                .Where(e => e.CustomerId == id);

            bool fixedDiscountApplied = false;

            decimal subtotal = 0m;

            foreach (var cart in cartlist)
            {
                decimal originalPrice = (decimal)(cart.Product.AfterDiscountPrice ?? 0m);
                decimal discountedPrice = originalPrice;

                if (discount.Type == application1.Models.Products.Coupon.DiscountType.Percentage)
                {
                    discountedPrice -= (originalPrice * discount.DiscountValue / 100);
                }
                else if (discount.Type == application1.Models.Products.Coupon.DiscountType.FixedAmount)
                {
                    if (!fixedDiscountApplied)
                    {
                        discountedPrice -= discount.DiscountValue;
                        if (discountedPrice < 0) discountedPrice = 0;
                        fixedDiscountApplied = true;
                    }
                }

                subtotal += discountedPrice * cart.Quantity;

                var sessionitem = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(discountedPrice * 100),
                        Currency = "myr",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = cart.Product.ProductName,
                        },
                    },
                    Quantity = cart.Quantity
                };

                options.LineItems.Add(sessionitem);
            }

            // 加入运费
            var totalWeight = cartlist.Sum(x => x.Quantity * 0.5m);
            var deliveryModel = new DeliveryDetailSchema
            {
                DeliveryRegion = DeliveryDetailSchema.Region.WestMalaysia,
                WeightInKg = totalWeight
            };
            var deliveryFee = deliveryModel.CalculateDeliveryFee();
            if (deliveryFee > 0)
            {
                subtotal += deliveryFee;

                var deliveryItem = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(deliveryFee * 100),
                        Currency = "myr",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Delivery Fee"
                        }
                    },
                    Quantity = 1
                };
                options.LineItems.Add(deliveryItem);
            }

            // 计算 SST（8%）
            var sst = subtotal * 0.08m;
            var sstItem = new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    UnitAmount = (long)(sst * 100),
                    Currency = "myr",
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = "SST",
                    },
                },
                Quantity = 1
            };
            options.LineItems.Add(sstItem);

            var service = new SessionService();
            Session session = service.Create(options);
            Response.Headers.Add("location", session.Url);
            return new StatusCodeResult(303);

        }
        public IActionResult paymentsucersspage()
        {
            var GetCustomerLogin = GetLoginAdmin().Result;
            if (GetCustomerLogin == null)
                return RedirectToAction("login", "custermor");
            var cart = db.CartList.Include(x => x.Product)
              .Where(x => x.CustomerId == GetCustomerLogin.Id)
              .Select(x => new CartAndProduct
              {
                  CartList = x,
                  Product = x.Product
              });
            var subttottal = (cart.Where(x => x.Product.Status == ProductStatus.Active).Sum(x => x.CartList.Quantity * x.Product.AfterDiscountPrice));

            TransactionHistory transhihisoty = new TransactionHistory()
            {
                SubTotal = subttottal.ToString(),
                SST = (subttottal * 0.08m).ToString(),
                GrandTotal = (subttottal * 1.08m).ToString(),
                PaymentTime = DateTime.Now,
                CustomerId = GetCustomerLogin.Id,
                Status = TransactionStatus.Paid
            };
            db.TransactionHistory.Add(transhihisoty);
            db.SaveChanges();

            var groupedBySeller = cart
             .Where(x => x.Product.Status == ProductStatus.Active)
             .GroupBy(x => x.Product.AdminId); // 按卖家分组

            foreach (var sellerGroup in groupedBySeller)
            {
                var adminId = sellerGroup.Key;
                var subtotal = sellerGroup.Sum(x => x.CartList.Quantity * x.Product.AfterDiscountPrice)?? 0m;
                var platformFee = subtotal * 0.15m;
                var sellerAmount = subtotal - platformFee;

                PlatformFeeAmount platformFeeAmount = new PlatformFeeAmount()
                {
                    TransactionHistoryId = transhihisoty.Id,
                    AdminId = adminId,
                    Amount = subtotal,
                    PlatformFee = platformFee,
                    SellerAmount = sellerAmount,
                    TransactionDate = DateTime.Now,
                };

                db.PlatformFeeAmount.Add(platformFeeAmount);
            }

            db.SaveChanges();

            var details = new List<TransactionDetail>();
            foreach (var item in cart)
            {
                var productId = item.Product.Id;
                var detail = new TransactionDetail
                {
                    TransactionHistoryId = transhihisoty.Id,
                    ProductId = productId,
                    QTY = item.CartList.Quantity,
                    Price = item.Product.AfterDiscountPrice.Value
                };
                db.TransactionDetail.Add(detail);
                db.SaveChanges();
                details.Add(detail);
                item.Product.ProductStock -= item.CartList.Quantity;
                //db.products.Update(item.products);
                db.SaveChanges();

            }
            var cartItemsToRemove = db.CartList.Where(x => x.CustomerId == GetCustomerLogin.Id);
            db.CartList.RemoveRange(cartItemsToRemove);
            db.SaveChanges();
            var customerProfile = db.CustomerProfiles.FirstOrDefault(x => x.Id == GetCustomerLogin.CustomerProfileId);
            var address = customerProfile?.Adress; 

            foreach (var item in details)
            {
                var deliveryDetail = new DeliveryDetailSchema
                {
                    DeliveryAdress = customerProfile.Adress,
                    DeliveryTime = DateTime.Now.AddDays(1),
                    Status = DeliveryDetailSchema.DeliveryStatus.Pending,
                    DeliveryConfimation = false,
                    ReturnDelivery = false,
                    TransactionHistoryId = transhihisoty.Id,
                    TransactionDetailId = item.Id,
                    DeliveryRegion = DeliveryDetailSchema.Region.WestMalaysia, 
                    WeightInKg = 0.5m * item.QTY,
                };


                // Auto-calculate delivery fee
                deliveryDetail.DeliveryFee = deliveryDetail.CalculateDeliveryFee();

                db.Add(deliveryDetail);
            }
            db.SaveChanges();

            var emailBody = new StringBuilder();
            emailBody.AppendLine("<style>");
            emailBody.AppendLine("table { width: 100%; border-collapse: collapse; }");
            emailBody.AppendLine("th, td { border: 1px solid #dddddd; text-align: left; padding: 8px; }");
            emailBody.AppendLine("th { background-color: #f2f2f2; }");
            emailBody.AppendLine("img { width: 100px; height: 100px; }");
            emailBody.AppendLine("</style>");

            emailBody.AppendLine("<h1>Transaction Details:</h1>");
            emailBody.AppendLine("<table>");
            emailBody.AppendLine("<tr><th>Subtotal</th><td>RM" + transhihisoty.Status + "</td></tr>");
            emailBody.AppendLine("<tr><th>SST</th><td>RM" + transhihisoty.SST + "</td></tr>");
            emailBody.AppendLine("<tr><th>Grand Total</th><td>RM" + transhihisoty.GrandTotal + "</td></tr>");
            emailBody.AppendLine("<tr><th>Time</th><td>" + transhihisoty.PaymentTime + "</td></tr>");
            emailBody.AppendLine("</table>");

            emailBody.AppendLine("<h2>Details:</h2>");
            emailBody.AppendLine("<table>");
            emailBody.AppendLine("<tr>");
            emailBody.AppendLine("<th>Product ID</th>");
            emailBody.AppendLine("<th>Quantity</th>");
            emailBody.AppendLine("<th>Price</th>");
            emailBody.AppendLine("<th>Total Price</th>");
            emailBody.AppendLine("<th>Image</th>");
            emailBody.AppendLine("</tr>");


            foreach (var detail in details)
            {
                var product = detail.Product;
                if (product != null)
                {
                    decimal totalPrice = detail.Price * detail.QTY;
                    emailBody.AppendLine("<tr>");
                    emailBody.AppendLine("<td>" + detail.ProductId + "</td>");
                    emailBody.AppendLine("<td>" + detail.QTY + "</td>");
                    emailBody.AppendLine("<td>RM" + detail.Price.ToString("F2") + "</td>");
                    emailBody.AppendLine("<td>RM" + totalPrice.ToString("F2") + "</td>");
                    emailBody.AppendLine("</tr>");
                }
            }
            emailBody.AppendLine("</ul>");

            string email = "cristalvip368@gmail.com";
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse("candychia0907@gmail.com"));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = "Ivoice";
            message.Body = new TextPart(MimeKit.Text.TextFormat.Html)
            {
                Text = emailBody.ToString()
            };
            using var smtp = new SmtpClient();
           
            smtp.Connect("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            smtp.Authenticate("candychia0907@gmail.com", "hlpenmadoteezyco");
            smtp.Send(message);
            smtp.Disconnect(true);

            return View(transhihisoty);
        }
        public IActionResult paymenterrorpage()
        {
            return View();
        }
        public IActionResult trasnstionhistory()
        {
            var GetCustomerLogin = GetLoginAdmin().Result;
            if (GetCustomerLogin == null)
                return RedirectToAction("login", "custermor");

            var tanshHistories = db.TransactionHistory.Where(x => x.CustomerId == GetCustomerLogin.Id);

            var singleTanshHistory = tanshHistories;
            return View(singleTanshHistory);

        }

        public IActionResult transhtiondetail(int? id)
        {
           
            var product = db.TransactionDetail.Include(x => x.Product).Include(x => x.TransactionHistory)
                                     .Where(x => x.TransactionHistoryId == id)
                                     .Select(x => new DETAIL { detail = x, products = x.Product });


            if (product == null)
                return Json(new { success = false, message = "Product not found" });

            return Json(new { success = true, data = product });
        }
        public IActionResult DeliveryDetail()
        {
            var GetCustomerLogin = GetLoginAdmin().Result;

            var profile = db.CustomerProfiles.FirstOrDefault(x => x.Id == GetCustomerLogin.CustomerProfileId);
            if(profile.Adress == null)
            {
                return Json(new { check = false, msg = "Adress is Empty pls Go to profile to set" });
            }
            var cartItems = db.CartList.Include(x => x.Product).Where(x => x.CustomerId == GetCustomerLogin.Id).ToList();
            var totalWeight = cartItems.Sum(x => x.Quantity * 0.5m);

            var coupons = db.Coupon
                .Where(c => c.ExpirationDate >= DateTime.Now && c.UsageLimit > 0) 
                .ToList();

            var model = new DeliveryDetailSchema
            {
                DeliveryAdress = profile.Adress,
                DeliveryTime = DateTime.Now.AddDays(1),
                DeliveryRegion = DeliveryDetailSchema.Region.WestMalaysia,
                WeightInKg = totalWeight
            };

            model.DeliveryFee = model.CalculateDeliveryFee();

            ViewBag.Coupons = coupons;

            return View(model);
        }


        [HttpPost]
        public IActionResult DeliveryDetail(DeliveryDetailSchema model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = GetLoginAdmin().Result;
            if (user == null)
                return RedirectToAction("login", "custermor");

            HttpContext.Session.SetString("deliveryRegion", model.DeliveryRegion.ToString());
            HttpContext.Session.SetString("deliveryFee", model.CalculateDeliveryFee().ToString());

            return RedirectToAction("paymentcheckout", "Payment");
        }

        public IActionResult TranshitdeliveryDetail(int id)
        {
            var result = db.DeliveryDetailSchema.Where(x=>x.TransactionHistoryId == id).ToList();
            return View(result);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReturnDelivery([FromBody] ReturnRequestModel model)
        {
            if (model?.Ids == null || !model.Ids.Any())
                return BadRequest();

            var deliveryItems = db.DeliveryDetailSchema
                .Where(x => model.Ids.Contains(x.Id))
                .ToList();

            foreach (var item in deliveryItems)
            {
                item.ReturnDelivery = !item.ReturnDelivery; 
            }

            db.SaveChanges();

            return Ok();
        }

        public  IActionResult DeliveyStatus()
        {
            var result = db.DeliveryDetailSchema.ToList();
            return View(result);
        }

        public class ReturnRequestModel
        {
            public List<int> Ids { get; set; }
        }


    }
}
