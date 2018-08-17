using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;

using System;
using System.ComponentModel;
using System.Numerics;

namespace NeoChess
{
    /**
     * smart contract for Auction
     * @author Clyde
     */
    public class NeoChess : SmartContract
    {

        // SGAS合约hash
        //[Appcall("e52a08c20986332ad8dccf9ded38cc493878064a")]
        //static extern object nep55Call(string method, object[] arr);
        delegate object deleDyncall(string method, object[] arr);

        // the owner, super admin address
        public static readonly byte[] ContractOwner = "ATTBr4BMvv5AiGPiLRuAHpENhiYSE4ykBG".ToScriptHash();

        // 有权限发行0代合约的钱包地址
        public static readonly byte[] MintOwner = "ATNsFEgwioorMbnf8UU21PoHq2HQZ8Q6HG".ToScriptHash();




        [Serializable]
        public class AuctionInfo
        {
            public byte[] owner;
            // 0拍卖 1克隆拍卖
            public int sellType;
            public uint sellTime;
            public BigInteger beginPrice;
            public BigInteger endPrice;
            public BigInteger duration;
        }



        //notify 购买通知
        public delegate void deleAuctionBuy(byte[] buyer, BigInteger tokenId, BigInteger curBuyPrice, BigInteger fee, BigInteger nowtime);
        [DisplayName("auctionBuy")]
        public static event deleAuctionBuy AuctionBuy;


        /**
         * Name
         */
        public static string name()
        {
            return "chess";
        }
        /**
          * 版本
          */
        public static string Version()
        {
            return "1.1.0";
        }


        /**
         * 存储增加的代币数量
         */
        private static void _addTotal(BigInteger count)
        {
            BigInteger total = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
            total += count;
            Storage.Put(Storage.CurrentContext, "totalExchargeSgas", total);
        }
        /**
         * 不包含收取的手续费在内，所有用户存在拍卖行中的代币
         */
        public static BigInteger totalExchargeSgas()
        {
            return Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
        }


        /**
         * 存储减少的代币数总量
         */
        private static void _subTotal(BigInteger count)
        {
            BigInteger total = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
            total -= count;
            if (total > 0)
            {
                Storage.Put(Storage.CurrentContext, "totalExchargeSgas", total);
            }
            else
            {

                Storage.Delete(Storage.CurrentContext, "totalExchargeSgas");
            }
        }

        /**
         * 用户在拍卖所存储的代币
         */
        public static BigInteger balanceOf(byte[] address)
        {
            //2018/6/5 cwt 修补漏洞
            byte[] keytaddress = new byte[] { 0x11 }.Concat(address);
            return Storage.Get(Storage.CurrentContext, keytaddress).AsBigInteger();
        }

        /**
         * 该txid是否已经充值过
         */
        public static bool hasAlreadyCharged(byte[] txid)
        {
            //2018/6/5 cwt 修补漏洞
            byte[] keytxid = new byte[] { 0x11 }.Concat(txid);
            byte[] txinfo = Storage.Get(Storage.CurrentContext, keytxid);
            if (txinfo.Length > 0)
            {
                // 已经处理过了
                return false;
            }
            return true;
        }

        /**
         * 使用txid充值
         */
        public static bool rechargeToken(byte[] owner, byte[] txid)
        {
            if (owner.Length != 20)
            {
                Runtime.Log("Owner error.");
                return false;
            }

         
            byte[] keytxid = new byte[] { 0x11 }.Concat(txid);
            byte[] keytowner = new byte[] { 0x11 }.Concat(owner);

            byte[] txinfo = Storage.Get(Storage.CurrentContext, keytxid);
            if (txinfo.Length > 0)
            {
                // 已经处理过了
                return false;
            }


            // 查询交易记录
            object[] args = new object[1] { txid };
            byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
            deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
            object[] res = (object[])dyncall("getTxInfo", args);

            if (res.Length > 0)
            {
                byte[] from = (byte[])res[0];
                byte[] to = (byte[])res[1];
                BigInteger value = (BigInteger)res[2];

                if (from == owner)
                {
                    if (to == ExecutionEngine.ExecutingScriptHash)
                    {
                        // 标记为处理
                        Storage.Put(Storage.CurrentContext, keytxid, value);

                        BigInteger nMoney = 0;
                        byte[] ownerMoney = Storage.Get(Storage.CurrentContext, keytowner);
                        if (ownerMoney.Length > 0)
                        {
                            nMoney = ownerMoney.AsBigInteger();
                        }
                        nMoney += value;

                        _addTotal(value);

                        // 记账
                        Storage.Put(Storage.CurrentContext, keytowner, nMoney.AsByteArray());
                        return true;
                    }
                }
            }
            return false;
        }

        /**
         * 提币
         */
        public static bool drawToken(byte[] sender, BigInteger count)
        {
            if (sender.Length != 20)
            {
                Runtime.Log("Owner error.");
                return false;
            }

            //2018/6/5 cwt 修补漏洞
            byte[] keytsender = new byte[] { 0x11 }.Concat(sender);

            if (Runtime.CheckWitness(sender))
            {
                BigInteger nMoney = 0;
                byte[] ownerMoney = Storage.Get(Storage.CurrentContext, keytsender);
                if (ownerMoney.Length > 0)
                {
                    nMoney = ownerMoney.AsBigInteger();
                }
                if (count <= 0 || count > nMoney)
                {
                    // 全部提走
                    count = nMoney;
                }

                // 转账
                object[] args = new object[3] { ExecutionEngine.ExecutingScriptHash, sender, count };
                byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
                deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
                bool res = (bool)dyncall("transferAPP", args);
                if (!res)
                {
                    return false;
                }

                // 记账
                nMoney -= count;

                _subTotal(count);

                if (nMoney > 0)
                {
                    Storage.Put(Storage.CurrentContext, keytsender, nMoney.AsByteArray());
                }
                else
                {
                    Storage.Delete(Storage.CurrentContext, keytsender);
                }

                return true;
            }
            return false;
        }



      

        /**
         * 从拍卖场购买道具,将钱划入合约名下，将物品给买家
         */
        public static bool buyOnAuction(byte[] sender, BigInteger tokenId)
        {
            if (!Runtime.CheckWitness(sender))
            {
                //没有签名
                return false;
            }

            object[] objInfo = _getAuctionInfo(tokenId.AsByteArray());
            if (objInfo.Length > 0)
            {
                AuctionInfo info = (AuctionInfo)(object)objInfo;
                byte[] owner = info.owner;

                var nowtime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
                var secondPass = nowtime - info.sellTime;
                //var secondPass = (nowtime - info.sellTime) / 1000;
                //2018/6/5 cwt 修补漏洞
                byte[] keytsender = new byte[] { 0x11 }.Concat(sender);
                byte[] keytowner = new byte[] { 0x11 }.Concat(owner);

                BigInteger senderMoney = Storage.Get(Storage.CurrentContext, keytsender).AsBigInteger();
                BigInteger curBuyPrice = computeCurrentPrice(info.beginPrice, info.endPrice, info.duration, secondPass);
                var fee = curBuyPrice * 199 / 10000;
                if (fee < TX_MIN_FEE)
                {
                    fee = TX_MIN_FEE;
                }
                if (curBuyPrice < fee)
                {
                    curBuyPrice = fee;
                }

                if (senderMoney < curBuyPrice)
                {
                    // 钱不够
                    return false;
                }


                // 转移物品
                object[] args = new object[3] { ExecutionEngine.ExecutingScriptHash, sender, tokenId };
                bool res = (bool)nftCall("transfer_app", args);
                if (!res)
                {
                    return false;
                }

                // 扣钱
                Storage.Put(Storage.CurrentContext, keytsender, senderMoney - curBuyPrice);

                // 扣除手续费
                BigInteger sellPrice = curBuyPrice - fee;
                _subTotal(fee);

                // 钱记在卖家名下
                BigInteger nMoney = 0;
                byte[] salerMoney = Storage.Get(Storage.CurrentContext, keytowner);
                if (salerMoney.Length > 0)
                {
                    nMoney = salerMoney.AsBigInteger();
                }
                nMoney = nMoney + sellPrice;
                Storage.Put(Storage.CurrentContext, keytowner, nMoney);

                // 删除拍卖记录
                Storage.Delete(Storage.CurrentContext, tokenId.AsByteArray());
                
                // notify
                AuctionBuy(sender, tokenId, curBuyPrice, fee, nowtime);
                return true;

            }
            return false;
        }

       

        


        
      
        /**
         * 将收入提款到合约拥有者
         */
        public static bool drawToContractOwner(BigInteger flag, BigInteger count)
        {
            if (Runtime.CheckWitness(ContractOwner))
            {
                BigInteger nMoney = 0;
                // 查询余额
                object[] args = new object[1] { ExecutionEngine.ExecutingScriptHash };
                byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
                deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
                BigInteger totalMoney = (BigInteger)dyncall("balanceOf", args);
                BigInteger supplyMoney = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();
                if (flag == 0)
                {
                    BigInteger canDrawMax = totalMoney - supplyMoney;
                    if (count <= 0 || count > canDrawMax)
                    {
                        // 全部提走
                        count = canDrawMax;
                    }
                }
                else
                {
                    //由于官方SGAS合约实在太慢，为了保证项目上线，先发行自己的SGAS合约方案，预留出来迁移至官方sgas用的。
                    count = totalMoney;
                    nMoney = 0;
                    Storage.Put(Storage.CurrentContext, "totalExchargeSgas", nMoney);
                }
                // 转账
                args = new object[3] { ExecutionEngine.ExecutingScriptHash, ContractOwner, count };

                deleDyncall dyncall2 = (deleDyncall)sgasHash.ToDelegate();
                bool res = (bool)dyncall2("transferAPP", args);
                if (!res)
                {
                    return false;
                }

                // 记账  cwt此处不应该记账
                //_subTotal(count);
                return true;
            }
            return false;
        }

        public static BigInteger getAuctionAllFee()
        {
            BigInteger nMoney = 0;
            // 查询余额
            object[] args = new object[1] { ExecutionEngine.ExecutingScriptHash };
            byte[] sgasHash = Storage.Get(Storage.CurrentContext, "sgas");
            deleDyncall dyncall = (deleDyncall)sgasHash.ToDelegate();
            BigInteger totalMoney = (BigInteger)dyncall("balanceOf", args);
            BigInteger supplyMoney = Storage.Get(Storage.CurrentContext, "totalExchargeSgas").AsBigInteger();

            BigInteger canDrawMax = totalMoney - supplyMoney;
            return canDrawMax;
        }

        /**
         * 合约入口
         */
        public static Object Main(string method, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification) //取钱才会涉及这里
            {
                if (ContractOwner.Length == 20)
                {
                    // if param ContractOwner is script hash
                    //return Runtime.CheckWitness(ContractOwner);
                    return false;
                }
                else if (ContractOwner.Length == 33)
                {
                    // if param ContractOwner is public key
                    byte[] signature = method.AsByteArray();
                    return VerifySignature(signature, ContractOwner);
                }
            }
            else if (Runtime.Trigger == TriggerType.VerificationR)
            {
                return true;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //必须在入口函数取得callscript，调用脚本的函数，也会导致执行栈变化，再取callscript就晚了
                var callscript = ExecutionEngine.CallingScriptHash;

                if (method == "cloneOnAuction")
                {
                    if (args.Length != 3) return 0;
                    byte[] sender = (byte[])args[0];
                    BigInteger motherGlaId = (BigInteger)args[1];
                    BigInteger fatherGlaId = (BigInteger)args[2];

                    return cloneOnAuction(sender, motherGlaId, fatherGlaId);
                }

                if (method == "setGenoPrice")
                {
                    if (Runtime.CheckWitness(ContractOwner))
                    {
                        BigInteger maxPrice = (BigInteger)args[0];
                        BigInteger minPrice = (BigInteger)args[1];
                        BigInteger duration = (BigInteger)args[2];
                        GenoPrice gp = new GenoPrice();
                        gp.max_price = maxPrice;
                        gp.min_price = minPrice;
                        gp.duration = duration;
                        return _putGenoPrice(gp);
                    }
                    return false;
                }
                if (method == "getGenoPrice")
                {
                    return getGenoPrice();
                }

                if (method == "_setSgas")
                {
                    if (Runtime.CheckWitness(ContractOwner))
                    {
                        Storage.Put(Storage.CurrentContext, "sgas", (byte[])args[0]);
                        return new byte[] { 0x01 };
                    }
                    return new byte[] { 0x00 };
                }
                if (method == "getSgas")
                {
                    return Storage.Get(Storage.CurrentContext, "sgas");
                }
                //this is in nep5
                if (method == "totalExchargeSgas") return totalExchargeSgas();
                if (method == "version") return Version();
                if (method == "name") return name();
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return balanceOf(account);
                }
                if (method == "buyOnAuction")
                {
                    if (args.Length != 2) return 0;
                    byte[] owner = (byte[])args[0];
                    BigInteger tokenId = (BigInteger)args[1];

                    return buyOnAuction(owner, tokenId);
                }
                if (method == "drawToken")
                {
                    if (args.Length != 2) return 0;
                    byte[] owner = (byte[])args[0];
                    BigInteger count = (BigInteger)args[1];

                    return drawToken(owner, count);
                }

                if (method == "drawToContractOwner")
                {
                    if (args.Length != 2) return 0;
                    BigInteger flag = (BigInteger)args[0];
                    BigInteger count = (BigInteger)args[1];

                    return drawToContractOwner(flag, count);
                }
                if (method == "getAuctionAllFee")
                {
                    return getAuctionAllFee();
                }
                if (method == "rechargeToken")
                {
                    if (args.Length != 2) return 0;
                    byte[] owner = (byte[])args[0];
                    byte[] txid = (byte[])args[1];

                    return rechargeToken(owner, txid);
                }

                if (method == "hasAlreadyCharged")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];

                    return hasAlreadyCharged(txid);
                }

                if (method == "getAuctionRecord")
                {
                    if (args.Length != 1)
                        return 0;
                    byte[] txid = (byte[])args[0];
                    return getAuctionRecord(txid);
                }
                if (method == "upgrade")//合约的升级就是在合约中要添加这段代码来实现
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(ContractOwner))
                        return false;

                    if (args.Length != 1 && args.Length != 9)
                        return false;

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script)
                        return false;

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)05;
                    string name = "Auction";
                    string version = "1.1";
                    string author = "CG";
                    string email = "0";
                    string description = "test";

                    if (args.Length == 9)
                    {
                        parameter_list = (byte[])args[1];
                        return_type = (byte)args[2];
                        need_storage = (bool)args[3];
                        name = (string)args[4];
                        version = (string)args[5];
                        author = (string)args[6];
                        email = (string)args[7];
                        description = (string)args[8];
                    }
                    Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
                    return true;
                }
            }
            return false;
        }

        /**
		 * Computes the current price of an auction.
		 * @param startingPrice
		 * @param endingPrice
		 * @param duration
		 * @param secondsPassed
		 * @return 
		 */
        private static BigInteger computeCurrentPrice(BigInteger beginPrice, BigInteger endingPrice, BigInteger duration, BigInteger secondsPassed)
        {
            if (duration < 1)
            {
                // 避免被0除
                duration = 1;
            }

            if (secondsPassed >= duration)
            {
                // We've reached the end of the dynamic pricing portion
                // of the auction, just return the end price.
                return endingPrice;
            }
            else
            {
                // Starting price can be higher than ending price (and often is!), so
                // this delta can be negative.
                //var totalPriceChange = endingPrice - beginPrice;

                // This multiplication can't overflow, _secondsPassed will easily fit within
                // 64-bits, and totalPriceChange will easily fit within 128-bits, their product
                // will always fit within 256-bits.
                //var currentPriceChange = totalPriceChange * secondsPassed / duration;
                //var currentPrice = beginPrice + (endingPrice - beginPrice) * secondsPassed / duration; 
                return beginPrice + (endingPrice - beginPrice) * secondsPassed / duration;
            }
        }

        /**
         * 获取拍卖信息
         */
        private static object[] _getAuctionInfo(byte[] tokenId)
        {

            byte[] v = Storage.Get(Storage.CurrentContext, tokenId);
            if (v.Length == 0)
                return new object[0];

            /*
            //老式实现方法
            AuctionInfo info = new AuctionInfo();
            int seek = 0;
            var ownerLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.owner = v.AsString().Substring(seek, ownerLen).AsByteArray();
            seek += ownerLen;

            int dataLen;
            dataLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.sellType = v.AsString().Substring(seek, dataLen).AsByteArray().AsBigInteger();

            dataLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.beginPrice = v.AsString().Substring(seek, dataLen).AsByteArray().AsBigInteger();

            dataLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.endPrice = v.AsString().Substring(seek, dataLen).AsByteArray().AsBigInteger();

            dataLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.duration = v.AsString().Substring(seek, dataLen).AsByteArray().AsBigInteger();

            return (object[])(object)info;
            */

            //新式实现方法只要一行
            return (object[])Helper.Deserialize(v);
        }

        /**
         * 存储拍卖信息
         */
        private static void _putAuctionInfo(byte[] tokenId, AuctionInfo info)
        {
            /*
            // 用一个老式实现法
            byte[] auctionInfo = _ByteLen(info.owner.Length).Concat(info.owner);
            auctionInfo = auctionInfo.Concat(_ByteLen(info.sellType.AsByteArray().Length)).Concat(info.sellType.AsByteArray());
            auctionInfo = auctionInfo.Concat(_ByteLen(info.beginPrice.AsByteArray().Length)).Concat(info.beginPrice.AsByteArray());
            auctionInfo = auctionInfo.Concat(_ByteLen(info.endPrice.AsByteArray().Length)).Concat(info.endPrice.AsByteArray());
            auctionInfo = auctionInfo.Concat(_ByteLen(info.duration.AsByteArray().Length)).Concat(info.duration.AsByteArray());
            */
            // 新式实现方法只要一行
            byte[] auctionInfo = Helper.Serialize(info);

            Storage.Put(Storage.CurrentContext, tokenId, auctionInfo);
        }

        /**
         * 删除存储拍卖信息
         */
        private static void _delAuctionInfo(byte[] tokenId)
        {
            Storage.Delete(Storage.CurrentContext, tokenId);
        }

        /**
         * 获取拍卖成交记录
         */
        public static object[] getAuctionRecord(byte[] tokenId)
        {
            var key = "buy".AsByteArray().Concat(tokenId);
            byte[] v = Storage.Get(Storage.CurrentContext, key);
            if (v.Length == 0)
            {
                return new object[0];
            }

            /*
            //老式实现方法
            AuctionRecord info = new AuctionRecord();
            int seek = 0;
            var ownerLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.seller = v.AsString().Substring(seek, ownerLen).AsByteArray();
            seek += ownerLen;

            ownerLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.buyer = v.AsString().Substring(seek, ownerLen).AsByteArray();
            seek += ownerLen;

            int dataLen;
            dataLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.sellPrice = v.AsString().Substring(seek, dataLen).AsByteArray().AsBigInteger();

            dataLen = (int)v.AsString().Substring(seek, 2).AsByteArray().AsBigInteger();
            seek += 2;
            info.sellTime = v.AsString().Substring(seek, dataLen).AsByteArray().AsBigInteger();

            return (object[])(object)info;
            */

            //新式实现方法只要一行
            return (object[])Helper.Deserialize(v);
        }

        /**
         * 存储拍卖成交记录
         */
        private static void _putAuctionRecord(byte[] tokenId, AuctionRecord info)
        {
            /*
            // 用一个老式实现法
            byte[] auctionInfo = _ByteLen(info.seller.Length).Concat(info.seller);
            auctionInfo = _ByteLen(info.buyer.Length).Concat(info.buyer);
            auctionInfo = auctionInfo.Concat(_ByteLen(info.sellPrice.AsByteArray().Length)).Concat(info.sellPrice.AsByteArray());
            auctionInfo = auctionInfo.Concat(_ByteLen(info.sellTime.AsByteArray().Length)).Concat(info.sellTime.AsByteArray());
            */
            // 新式实现方法只要一行
            byte[] txInfo = Helper.Serialize(info);

            var key = "buy".AsByteArray().Concat(tokenId);
            Storage.Put(Storage.CurrentContext, key, txInfo);
        }


       


    }
}
